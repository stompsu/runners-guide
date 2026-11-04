# ADR 0005 — Deterministic insights and owner-scoped API

- Status: Accepted (M5.1/M5.2 backend)
- Date: 2026-11-04

## Context

Insights must be reproducible, expose incomplete diary coverage honestly, and never
mix owners' data. They are descriptive diary statistics, not medical advice.
This backend decision introduces no AI, persisted aggregates, migrations, or
cloud provisioning.

## Decision

### Boundaries, queries, and contracts

- Pure `Domain/Insights/InsightsCalculator` consumes immutable scalar completed
  sessions and explicit UTC dates/first activity date. No EF, clocks, network,
  entity graphs, or random values are involved in domain calculations.
- The authenticated Insights vertical slice captures `TimeProvider.GetUtcNow()`
  once per request, including current-user/guest expiry validation. It reuses the
  existing current-user behavior (including non-guest first-use registration).
  Concurrent first-use registrations reload the winning non-guest owner; write
  failures without a matching owner continue to propagate.
- Before projection or aggregation, EF queries filter owner, completed status,
  date bounds, and exclude any persisted track with `DeleteParentRun=true`.
  Track-only pending deletion does **not** exclude its parent run. Earliest
  activity queries use the same ownership/status/delete rules and stop at `to`.
- Choose no-tracking **scalar projection followed by pure UTC day/type grouping**,
  rather than provider-dependent server-side compound statistical grouping.
  There are two insights queries: bounded numerical scalar rows and a scalar
  minimum first-activity timestamp across history. All charts share these rows.
  This avoids entity loading, sensitive columns, chart N+1, and InMemory/SQL
  grouping differences. The lookback is the earliest of selected `from`, January 1,
  `from - 27`, previous week start, and previous month start. There is no unbounded
  history materialization; row volume still depends on sessions in that window.
  If volume warrants it later, introduce equivalent server-side day/type
  sufficient statistics without changing metric semantics.
- `GET /api/insights?from=YYYY-MM-DD&to=YYYY-MM-DD&bucket=week|month`:
  dates required, year >= 2 (room for preceding periods), no future UTC dates,
  ascending inclusive range of at most 367 days. `bucket` is required.
  Non-nullable query bindings mark `from`/`to`/`bucket` (and race-history `to`)
  required in OpenAPI. Missing required bindings return HTTP 400; explicit
  semantic validation returns validation problems. Minimal API binding failures
  do not throw into the generic exception handler, including in Development.
- `GET /api/insights/races?to=YYYY-MM-DD&pageSize=25&cursor=…`:
  required inclusive non-future `to`; page size 1–100, default 25. One query
  selects at most page size + 1 scalars. Sort descending UTC start, then ID;
  keyset comparisons use the database's native ID ordering consistently.
  All completed `IsRace` sessions are returned even without `RaceDetails`.
  Return only ID, timestamp/type, distance/duration, race name/category,
  result time and placing; never notes, location, rating comments, GPX, or URLs.
- Cursor text is bounded to 2048 characters, versioned and authenticated/encrypted
  with ASP.NET Core Data Protection, with the owner as a protection purpose and
  `to` inside the protected payload. Foreign, changed-date, corrupt or oversized
  cursors return 400, not another owner's data. Page size can change between
  pages. This is keyset navigation, **not** a historical snapshot: concurrent
  edits/deletes can change the remaining results. Production replicas must share
  a durable Data Protection key ring for cursors to survive replica/restart
  changes; no cloud/key-ring provisioning is performed here.
- Handler responses use `Cache-Control: no-store`; both routes require the
  existing authorization pipeline, including guests and expiry rules. No guest
  quota is consumed by these read-only endpoints.
- Explicit typed DTOs, string enums, nullable derived values and reason enums
  are exposed in OpenAPI. Frontend TypeScript generation is deferred to the
  main integration flow.

### Metric semantics

1. **Eligibility/time:** completed only; UTC `StartAtUtc` calendar date, not
   local date or finish date. Monday-start ISO weeks include ISO week-year.
   Partial first/last buckets are clipped to the selected range and labelled
   with their natural period start; explicit empty buckets and daily gaps are
   returned. YTD is January 1 through `to`, independent of selected `from`.
2. **Classification:** catalog Training and `IsRace` are running;
   `cross-training` counts toward activity duration, gain/loss and load, but
   never running distance. `rest-injury` contributes nothing even if numerical
   fields were stored. Each period's totals expose a separate `restSessionCount`
   for completed rest/injury entries; `sessionCount` remains active-only. Rest
   counts use the same owner/date/delete filters and never enter metric coverage
   or the active distribution denominator. Distribution counts completed non-rest sessions by type,
   including cross-training, with shares in [0,1] summing to one when nonempty;
   no sessions means an empty distribution, not division by zero.
3. **Coverage:** each aggregate metric has `knownSubtotal`, `eligibleCount`,
   `contributingCount`, `missingCount`, and `isComplete`. Counts are sessions,
   not days. Supplied zero is a known value, not missing. Empty eligible sets
   have zero subtotal/counts and complete coverage; calendar gaps have zero
   load. Missing measurements are never estimates or zero-filled session loads.
   Missing distance does not invalidate known duration/load, and vice versa.
   Nonfinite/negative numeric values are excluded as missing; the domain also
   rejects implausibly unbounded (>1e20) double measurements defensively.
   Decimal subtotal arithmetic retains fractional minutes; derived doubles are
   checked for finiteness. No JSON NaN/Infinity is emitted.
4. **Load:** `durationSeconds * RPE / 60`, without rounding. Duration or RPE
   absent makes that session's load missing; RPE must be 1–10. Both zero-duration
   known load and incomplete load retain their distinct coverage.
5. **ACWR:** A7 is the inclusive `[to-6,to]` sum. C28 is the inclusive
   `[to-27,to]` sum and chronic weekly load is C28/4. The windows are
   **coupled** (A7 is included in C28). ACWR is A7/(C28/4).
   Any missing session load in the 28 days makes ACWR unavailable with
   `missingData`; known subtotals and coverage remain visible.
   Zero denominator gives `zeroChronicLoad`; zero acute and positive chronic
   gives valid ratio 0.
   `loadSeries` returns one point per selected UTC date, including gaps, with
   `date`, `acute7`, `chronic28`, `chronicWeeklyKnownSubtotal`, `acwr`, `band`,
   `bandReason` and `monotony`. Every point uses these same rules anchored to its
   own date (including per-date maturity). Daily coverage is computed once, then
   combined across trailing windows; future sessions never enter earlier points.
   Query lookback includes 27 days before selected `from`, even across years.
   The end snapshot `trainingLoad` uses the identical calculation as the last point.
6. **Bands:** `<0.8` low, `0.8..1.3` inclusive optimal, `>1.3` high.
   The ratio itself may exist earlier, but no band until 28 inclusive calendar
   dates since the first completed non-rest activity (`first + 27 <= to`).
   This is elapsed calendar time, not 28 logged sessions or 28 loaded days.
   Unavailable ratio reason takes precedence over `insufficientHistory`.
7. **Monotony:** mean of the last seven daily loads divided by **population**
   SD (divide squared deviations by seven). Missing load in those seven days
   gives `missingData`; all-zero gives `noLoad`; equal positive daily load
   gives `zeroVariance`. No-log days are zero and participate in the SD.
8. **Comparisons:** anchored to `to`, not selected `from`. Compare current
   Monday-through-`to` and month-start-through-`to` with the equivalent elapsed
   dates of the previous calendar period, clipped to its end. If `to` completes
   the current period, use the whole previous period (including unequal month
   lengths). Percentage = `(current - previous) / previous * 100`, unavailable
   for incomplete operands (`missingData`) or a zero baseline (`zeroBaseline`).
   Current and previous coverage/subtotals remain available.
9. **Easy pace:** exactly `easy-run`; only positive distance+duration pairs.
   Seconds/km = `sum(paired duration) / sum(paired distance) * 1000`, **not**
   mean of per-session paces. Return eligible easy-session and contributing
   pair counts, omission count and completeness. A partial paired estimate is
   returned with explicit incomplete coverage; no valid pairs gives
   `noEligibleSessions`.

## Verification and consequences

- Unit fixtures cover constant load (A7=420, C28=1680, ratio=1), fractional
  load, population monotony, band boundaries/maturity, coupled window edges,
  missing data, zero cases, classifications, weighted pace, ISO year changes,
  leap days, period-to-date comparisons and full unequal-length months.
- API integration tests use EF InMemory and exercise authentication,
  guest/non-guest ownership and expiry, validation, no-store, UTC dates,
  single clock capture, deletion intents, missing values/gaps, race pagination,
  cursor tampering/owner/date bounds and typed OpenAPI schemas.
- Offline SQL Server `ToQueryString` tests verify the actual scalar projections
  and initial/continuation keyset query translate, retain owner/delete filters,
  and omit sensitive columns. They **do not execute SQL**, verify query plans,
  validate index performance, exercise SQL isolation, or prove GUID ordering
  against a live database. InMemory behavior is not a substitute for these
  deployment-environment checks. No cloud connection or migration is used.
- Derived statistics intentionally withhold interpretation when coverage or
  history is inadequate. The API offers no risk diagnosis or AI narrative.
