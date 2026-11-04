# ADR 0004 — Azure Maps v3 and private GPX imports

- Status: accepted (M4 implementation)
- Date: 2026-11-04

## Context

M4 adds authenticated location selection, planned/recorded GPX previews, and explicit
completed-run metric suggestions. Guest and Entra app sessions must use the same
API authorization boundary. Map subscription keys must never reach browser code.
No resource provisioning, migration application, or deployment is part of M4.

## Decisions

### Map authentication and geocoding

Use `azure-maps-control` v3 behind `MapView`. The installed SDK types support
`AuthenticationType.anonymous`, `clientId`, and a `getToken(resolve, reject, map)`
callback. Here “anonymous” is the SDK's custom-token mode, **not** an anonymous
application endpoint.

`GET /api/maps/token` requires the existing app bearer authentication. The API
uses `DefaultAzureCredential` with `https://atlas.microsoft.com/.default`, returns
the Maps account client ID, token, and expiry, and sets `Cache-Control: no-store`.
The browser holds the token only in the component/SDK's memory and asks the API
again when needed. Never log tokens or enable request/response-body logging for
this endpoint. No subscription-key fallback, SAS, or API tile proxy exists.
The SDK contacts Maps directly for tiles.

`GET /api/maps/geocode?query=…` authenticates and rate-limits callers (60 Maps
requests/account/minute; process-local limiter). It bounds input to 200 characters,
uses Azure Maps Search Address with limit 5 and a 10-second HTTP timeout, and returns
at most five `{ displayName, latitude, longitude }` results. The frontend boundary
is provider-neutral. Only a selected name and WGS84 position enter the existing
typed `RunInput`; no provider identifier or search response is persisted.

### GPX contract and deterministic processing

`POST /api/runs/{id}/track?planned=true|false` takes raw
`application/gpx+xml` (XML media types also accepted), not multipart or a URL.
No filename becomes a storage name. Maximum file/body size is **20 MiB**, enforced
both by the server's request feature where available and by counting bytes while
reading, including streams without `Content-Length`. The entire bounded document
is validated before any blob write.

Pure domain processing uses an `XmlReader` with DTDs prohibited, no resolver, and
a character bound. GPX 1.0/1.1 namespaces are accepted. Reject malformed XML, empty
tracks, missing/bad/non-finite/out-of-range coordinates, bad/non-finite elevations,
elevations outside -12,000…10,000 m, and more than 100,000 points. Extension content
is not displayed. Timestamps that are absent, invalid, or regress make duration
unavailable; all points must have nondecreasing timestamps for first-to-last
duration (whole seconds, floored). Equal timestamps are allowed. Offset-less
timestamps are interpreted as UTC, independent of server locale.

Distance uses a 6,371,000 m-radius haversine and does not bridge segment gaps.
Elevation gain/loss accepts changes of at least 3 m relative to the last accepted
anchor; missing elevations leave that anchor unchanged. Deterministic iterative
Douglas–Peucker (10 m tolerance in local metre space) preserves segment endpoints
and meaningful elevation anchors/predecessors. A five-million-evaluation budget
falls back to retaining remaining points, avoiding adversarial quadratic CPU.
The profile keeps every meaningful elevation anchor/predecessor plus a regular
sample (approximately 2,000 points for smooth tracks) and the final point.

The processed JSON is a GeoJSON `FeatureCollection` containing a `MultiLineString`
and metric properties, with foreign members `elevation` (cumulative distance and
nullable elevation) and `isPlannedCourse`. Singleton segments duplicate their point
to remain valid line geometry. Coordinates are `[longitude, latitude]`, never
reversed. The original XML, names, extensions, and arbitrary XML text are not part
of the processed response. OpenAPI generates the shared frontend contract; the
generator maps binary strings to `Blob` for the typed raw upload.

### Private storage and lifecycle

Each run has one active metadata row (its PK is `RunId`). Store original and
processed data as separate private blobs under a random GUID prefix. Check that
the existing container has no public access on writes/reads; never create or change
it. No public blob URL, SAS, or storage credential is returned.

Authenticated owner-scoped routes:

- `GET /api/runs/{id}/track`: processed preview, `no-store`;
- `GET /api/runs/{id}/track/original`: streamed attachment through the API, `no-store`;
- `DELETE /api/runs/{id}/track`: durably mark pending deletion, remove both blobs,
  then finalize metadata deletion. Repeated deletion resumes the pending operation;
  an owned run with no track returns 204.

Missing and foreign run/track IDs both return 404. Uploads and track/run deletion
acquire SQL transactions with serializable isolation and a per-user
`UPDLOCK,HOLDLOCK` on `Users`, serializing across replicas. Recheck run existence
under the lock. A persisted `DiaryUser.GpxUploadRequestCount` allows **ten accepted
upload requests over the guest lifetime**, including replacements. Demo-seeded
tracks do not consume this allowance; deletion never refunds it. Re-read the user
under the lock and increment the counter in the same transaction/save as upload
metadata. Invalid/rejected requests and failed writes/saves do not consume it.
This is not an active-row cap: six seeded tracks plus ten uploads may coexist.
The additive migration initializes counters to zero; past upload requests cannot
be reconstructed for already-existing, short-lived guests.
In-memory tests skip SQL transactions; live SQL race behavior needs isolated
integration validation.

Replacement preserves old blob references and artifacts until both new writes
and the DB commit succeed. Failures best-effort delete new artifacts using an
independent cleanup timeout. Only after commit are old artifacts best-effort
removed. `TrackDeletionService` commits `GpxTrack.IsPendingDeletion` before any
storage delete, in a separate transaction from finalization. Run deletion also
persists `DeleteParentRun`; track-only retries must never clear that flag.
Pending tracks return 404 from preview/original GET and reject replacement with
409. A second per-user locked transaction deletes both artifacts idempotently and
only then removes track metadata (and the parent run when flagged). Storage
failures retain pending rows, including partial blob deletion. A final DB
save/commit failure also leaves durable intent even if both blobs are already
absent. Repeated API deletes and the existing hourly `ExpiredGuestCleanupService`
worker resume pending operations for **all users**, not only expired guests.
Worker candidates are rechecked under lock so stale snapshots cannot delete
new uploads after a previous operation finished. Expired-guest track deletion
uses the same protocol before removing the user. An already-deleted run returns
404; repeating deletion has no further side effects.
Failed post-commit or failure-path cleanup can leave unreferenced blobs; there is
no distributed SQL/Blob transaction. Operational reconciliation of unreferenced
opaque prefixes is a documented caveat, not a startup/cloud mutation.

### User intent and demo data

Import changes track metadata/artifacts only. It never writes run metrics.
The editor offers an explicit “Apply suggested metrics and start location” button
only for completed runs with a recorded track. Missing duration leaves the manual
duration untouched. The user must then save via existing typed run update; existing
validation still applies (a long/large GPX may exceed supported run field limits).
A planned-course import never offers autofill. Save new runs before attaching GPX.
Deleting a track leaves existing run fields unchanged.

`seed/demo/synthetic-silesia.gpx` is fictional and contains no personal activity.
Configured guest seeding attaches private copies to up to six demo races; offline
tests/shell do not need Blob configuration. Fixture metrics deliberately do not
replace the illustrative calendar race metrics. Blob failures fail configured
guest seeding rather than returning a partially seeded session.

## Dependencies and verification

`azure-maps-control` v3 supplies the supported SDK, CSS, and TypeScript types;
no separate map-auth package is needed. No new backend dependency is required.
Tests use in-memory EF/storage, fake Maps OAuth credentials/HTTP, and mocked SDK
display. They exercise XML safety, size/point limits, metrics, privacy/ownership,
replacement/cleanup failure, lifetime guest request limits (including seeded
tracks and replacement exhaustion), targeted first/final deletion save failures,
API/worker recovery, import intent, geocoding, and OAuth
scope/expiry. Live Maps/RBAC/browser tiles and cross-process SQL locking are not
claimed as verified by these tests.

References (verified for the approved plan):

- https://learn.microsoft.com/en-us/azure/azure-maps/azure-maps-authentication
- https://learn.microsoft.com/en-us/azure/azure-maps/how-to-use-map-control
- https://learn.microsoft.com/en-us/azure/azure-maps/how-to-manage-authentication
