# Architecture

## M5.1/M5.2 — deterministic insights backend

Status: implemented and locally verified; live SQL execution/performance checks
remain environment-dependent. No new migrations, cloud changes, AI/M6 work, or
frontend/generated TypeScript changes are part of this slice.

- Pure `Domain/Insights/InsightsCalculator` owns all numerical rules. The
  authenticated Insights API captures UTC time once, validates inclusive ranges
  (maximum 367 days), and applies current-user/guest expiry, owner, completed,
  and M4 parent-delete-intent filters before scalar projection.
- Two no-tracking insights queries supply bounded measurement rows and the
  earliest completed non-rest timestamp. UTC day/type grouping happens in pure
  code, shared across all charts; no entity graphs or per-chart N+1 queries.
  Track-only pending deletion remains visible.
- `GET /api/insights?from=…&to=…&bucket=week|month` returns selected, daily,
  explicit empty ISO-Monday weekly/monthly buckets, YTD, anchored calendar
  comparisons, count distribution, paired weighted easy pace, and load metrics.
  `loadSeries` supplies daily acute/chronic coverage, weekly-equivalent chronic
  load, ACWR, band/reason and monotony using the exact end-snapshot calculation
  anchored to each date. Query lookback starts no later than `from - 27 days`;
  the frontend need not duplicate load formulas.
  `GET /api/insights/races?to=…&pageSize=25&cursor=…` uses bounded keyset
  pagination (1–100) ordered descending UTC start/ID, including races without
  details. Protected cursors are bound to owner and `to`; they are not snapshots.
  Both APIs return `no-store` and exclude notes/location/comments/GPX.
- Training and race count toward running distance. Cross-training contributes
  activity time/elevation/load only; rest/injury contributes zero. Each period's
  totals expose completed `restSessionCount` separately from active `sessionCount`,
  without adding rest entries to metric coverage or distribution shares. Load is
  seconds × RPE / 60 with fractional minutes. Known subtotals, eligible/provided/
  missing counts and completeness distinguish zero, gaps and missing sessions.
- ACWR = inclusive A7/(C28/4), coupled windows. Any missing load in 28 days
  suppresses the ratio; zero chronic gives a reasoned null. Bands are <0.8 low,
  [0.8,1.3] optimal, >1.3 high, suppressed until 28 inclusive calendar dates since
  first completed activity. Monotony uses seven daily loads and population SD:
  missing, all-zero, and equal-positive cases have distinct null reasons.
- Comparisons use current week/month elapsed dates through `to` versus prior
  elapsed dates (clipped); complete periods compare to the whole prior period.
  Zero baseline or incomplete coverage gives a reasoned null percentage.
  YTD starts January 1; distribution uses non-rest session-count shares.
  Exactly `easy-run` positive duration/distance pairs supply weighted seconds/km.
- Typed OpenAPI response DTOs include precise nullable values and reason/band
  enums. The checked-in TypeScript contract is generated from this API document.
- Unit/API tests and offline SQL Server translation tests pass. Translation
  checks do not execute SQL or validate live query plans/concurrency; InMemory
  pagination tests do not establish native SQL GUID ordering. Replica-stable
  race cursors require a shared durable Data Protection key ring in production;
  this change does not provision one.

See [ADR 0005](adr/0005-deterministic-insights-and-owner-scoped-api.md) for exact
metric semantics and trade-offs.

## M5.3 — Insights dashboard

Status: implemented and locally verified; live Entra and SQL execution remain
environment-dependent.

- Protected `/insights` provides bookmarkable UTC period and bucket controls,
  API-backed totals, comparisons, volume and pace charts, activity distribution,
  and paginated race history. Sign-in preserves valid Insights query parameters.
- Load trends use the API's per-date `loadSeries`: acute and weekly-equivalent
  chronic load share a chart with distinguishable lines; ACWR and monotony each
  have separate charts. Incomplete values remain gaps, with accessible tables
  showing the server's values, coverage and unavailable reasons.
- Race history links to its UTC diary date and opens that day's existing run
  panel. The page and Recharts are lazy-loaded; run mutations invalidate cached
  insights. No AI, migrations, or cloud resources are added.
- Frontend tests, typecheck, lint and production build pass. Vite reports the
  existing large app and Maps chunks; the Insights chart bundle remains lazy.

## M1 — skeleton

Status: implemented.

- Use a .NET 10 solution with a C# Aspire AppHost, API, and ServiceDefaults project. Aspire orchestrates the API and Vite development server; expose the Vite endpoint for local browser access. Azure SQL and Blob Storage remain external, already-provisioned resources. Do not add local containers or provisioning/deployment steps.
- Keep the API contract in ASP.NET Core Minimal APIs and publish its OpenAPI document. Generate TypeScript contract types from that document with `openapi-typescript` and use `openapi-fetch` as the typed HTTP client. Generated files are checked in and regenerated/verified in CI.
- The React + TypeScript + Vite shell uses React Router. M1 added no run-data behavior; M2 adds the authenticated API while the UI remains a shell.
- Local Azure configuration is supplied outside source control. Use `DefaultAzureCredential` with the developer's Entra identity; the API may perform read-only SQL and Blob connectivity checks against configured resources. It must not create resources, containers, schemas, or data as a side effect of startup or health checks.
- CI restores, builds, and tests .NET projects and installs, tests, and builds the web app without Azure credentials or containers.

### M1 decisions and remaining setup

- The only unauthenticated M1 infrastructure routes are Development-only probes (`/health/live`, `/health/ready`) and the development OpenAPI document. Liveness returns a fixed status and readiness only returns a standard health status; neither returns user data or diagnostic details. These routes are not mapped outside Development.
- Readiness verifies Azure SQL with a read-only `SELECT 1` and Blob Storage with a read-only container-properties request. It does not create resources, containers, schemas, or data. The AppHost waits only for liveness, so a developer can run the app shell without Azure configuration; the readiness probe reports unhealthy until dev resource settings and permissions are configured.
- The developer supplies the non-secret Azure resource identifiers with API user secrets, then authenticates with `DefaultAzureCredential`. Actual resource names and Entra permissions remain developer setup inputs; they are not required for CI.

See [ADR 0001](adr/0001-m1-skeleton-orchestration-and-api-client.md).

## M2 — domain and Runs API

Status: implemented; database migrations must be applied explicitly to an isolated non-production Azure SQL database.

- `RunnersDiary.Domain` contains run/user types and validation rules; `RunnersDiary.Infrastructure` maps them through EF Core to SQL Server using SqlClient `Active Directory Default`. Run locations are WGS84 `geography`; run type codes are seeded in the initial migration. Race details and GPX metadata are related records, with GPX upload/processing deferred to M4.
- Every `/api/runs` endpoint requires a validated bearer token. Entra External ID uses configured authority/audience validation. Guest JWTs use a separate HMAC-SHA256 signing key of at least 256 bits, a `guest` role, and a 24-hour expiry. Production startup requires guest signing-key and Entra configuration. Development may use an ephemeral key if none is configured.
- `POST /api/auth/guest` is the sole unauthenticated application endpoint and is rate-limited to five creations per observed client IP per 15-minute window. It returns a token but no user data. Forwarded client addresses are used only when their proxy is explicitly trusted through configuration. Guest creation seeds an isolated ~6-month history from relative-date JSON templates; expired guest records are deleted hourly. Existing GPX blobs are deleted before their rows so storage failures leave rows available for an idempotent retry.
- Run list dates are inclusive UTC calendar dates, limited to 367 days per request. CRUD lookups always filter by the validated token's user ID; missing or foreign run IDs both return 404.
- The development OpenAPI document describes bearer security for the run endpoints. The typed frontend client supports attaching the current access token but does not implement sign-in; UI auth is M3.

See [ADR 0002](adr/0002-domain-runs-auth-and-demo.md).

## M3 — Calendar UI

Status: implemented; live Entra/API verification requires the configured development environment.

- React Router protects `/diary` and `/diary/YYYY/MM`; unauthenticated bookmarks return to their month after sign-in. The public sign-in page offers MSAL popup sign-in and `POST /api/auth/guest`. Vite public SPA identifiers configure Entra, with a dedicated MSAL 5 `/auth-callback.html` redirect-bridge entry.
- Guest tokens, Entra token cache, app session, and TanStack Query data stay in memory. Guest expiry, silent-token interaction failures, and API 401 responses clear the session/cache. Sign-out also clears MSAL's memory cache. MSAL may use session storage for temporary protocol metadata, not the app's guest token.
- The existing `openapi-fetch` client is the sole backend transport. Generated M2 schemas supply all run/session contracts; only the stable seeded run-type lookup is maintained locally because M2 has no catalog endpoint.
- UTC calendar dates drive Monday-first grids, ISO week-years, inclusive full-week queries, and completed-only totals. Cross-training/rest do not contribute running distance. Bubbles use type colors, explicit status text, filled/dashed borders, and race markers.
- Radix Dialog plus existing CSS is the lightweight component alternative to shadcn/Tailwind. Panels trap focus, restore the opener, and close with Escape. The grid has row/column semantics, roving day focus, arrow/Home/End/Page navigation, and a horizontally scrollable narrow-screen layout.
- React Hook Form/Zod validate add/edit forms and map metric display units into generated SI inputs. Race metadata is editable; existing location coordinates are preserved without adding map/geocoding controls. Ratings/comments are submitted only for completed runs.
- English strings and Intl formatting are organized under `web/src/i18n.ts`; maps, GPX, and insights remain out of scope.

## M4 — Maps and GPX

Status: implemented locally; live Maps/RBAC and isolated SQL concurrency checks
remain environment-dependent. The additive `TrackProcessedArtifact` and
`GuestUploadQuotaAndPendingTrackDeletion` migrations are generated, **not applied**.
No resources are provisioned or changed by startup.

- Azure Maps Web SDK **v3** stays behind `web/src/maps/MapView.tsx`; start/finish
  markers, segmented GeoJSON track display, map pin selection, and a labelled SVG
  elevation profile share generated OpenAPI contracts. Location search returns
  provider-neutral names and WGS84 coordinates through the existing typed client.
- Authenticated `/api/maps/token` obtains short-lived Entra OAuth tokens server-side
  using `DefaultAzureCredential` and scope `https://atlas.microsoft.com/.default`.
  The SDK's `anonymous`/`getToken` callback consumes that token and the Maps account
  client ID. This is compatible with guest and Entra app sessions. No browser
  subscription keys, SAS links, tile proxy, or token persistence/logging.
- Raw GPX uploads go to `/api/runs/{id}/track?planned=…`, limited to 20 MiB at the
  request and stream boundaries. Pure domain parsing prohibits DTDs/external
  entities, bounds points/characters and simplification CPU, and validates all
  numeric inputs. Haversine distance, 3 m accepted-anchor elevation noise
  filtering, and all-point nondecreasing timestamp duration are deterministic.
- One active track per run; raw original and processed GeoJSON are separate
  random-named blobs in the existing private container. Owner-only processed and
  original retrieval goes through the API (`no-store`); no blob URLs are returned.
  SQL serializable transactions with per-user update locks protect replacements,
  deletion, and a persisted guest lifetime limit of ten accepted upload requests
  across API replicas. Replacements consume a request; demo seeding does not;
  deletion never refunds requests. The counter is reloaded under the lock and
  saved transactionally with metadata, not computed from active rows.
- Replacement keeps old artifacts/metadata until new writes and DB commit succeed.
  Failed writes/commits best-effort clean new blobs; successful replacement
  best-effort cleans old blobs. Deletion (including run/guest cleanup) first commits
  a durable `IsPendingDeletion` marker; run deletion also sets `DeleteParentRun`.
  Pending tracks are hidden from GET/original and cannot be replaced. A separate
  per-user locked transaction removes blobs before final metadata/run deletion.
  Both storage failures and final DB-save failures retain recoverable pending
  rows. Repeated API deletes resume intent; the existing hourly cleanup worker
  retries pending tracks for all users, preserving the parent-run flag and
  rechecking candidates under lock to avoid deleting a later upload.
- Import only returns a preview. Completed recorded tracks offer explicit
  suggestions in the editor; the user must apply and then save using the existing
  typed run update. Planned course imports never offer autofill. Missing duration
  does not erase manual duration. Synthetic demo GPX copies are seeded for races
  when Blob Storage is configured.

### Local Maps setup (no provisioning)

Set the **existing Maps account's client ID** (not the SPA app ID or subscription
key) in API user secrets as `Azure:Maps:ClientId`. The identity selected locally by
`DefaultAzureCredential` needs **Azure Maps Data Reader** scoped to that account;
the hosted API identity needs the same existing assignment. Identity and Maps
account tenant must align. An authorized operator must arrange any missing RBAC
assignment separately; application startup does not create it. The Blob identity
also needs existing container data read/write/delete permissions; the container
must already be private. No Maps secret belongs in `web/.env*`.

The SDK calls Maps directly from the browser using the issued bearer token and
account client ID. Verify existing account origin/CORS settings for the intended
frontend origin with an authorized operator if direct requests are rejected; do
not add a subscription-key fallback. Use only a local or isolated development
database/storage for manual uploads after explicit migration review/application.

### Verification and limitations

Offline tests cover parser edge cases, storage/DB failure compensation, partial
deletion and expired-guest retry, first/final deletion-save failures and API/worker
recovery, owner boundaries, lifetime guest upload-request limits, explicit
UI import/autofill, and fake OAuth/geocoding. They do not prove live tiles,
cross-tenant/RBAC setup, or SQL lock behavior. Post-commit/compensating cleanup
failure can leave unreferenced private blobs requiring operational reconciliation.
The process-local Maps request limiter is not a distributed quota. Large but
valid GPX metrics can exceed run-editor limits; explicit saves retain validation.

See [ADR 0004](adr/0004-maps-gpx-private-imports.md) for routes, GeoJSON shape,
processing rules, lifecycle ordering, and tradeoffs.
