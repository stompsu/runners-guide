# Runners Diary

Runners Diary is a private running diary for planning and tracking runs, races, training load, and insights. See [docs/PROJECT_BRIEF.md](docs/PROJECT_BRIEF.md) for product scope and milestone acceptance criteria.

## Local development

Requirements: .NET 10 SDK and Node.js 22.13 or Node.js 24 LTS available on `PATH`. Docker is not required or used by the Aspire AppHost.

1. Configure the already-provisioned, non-production Azure SQL Database and Blob Storage endpoints in .NET user secrets for the API:

   ```powershell
   dotnet user-secrets set "Azure:Sql:Server" "<server>.database.windows.net" --project src\RunnersDiary.Api
   dotnet user-secrets set "Azure:Sql:Database" "<database>" --project src\RunnersDiary.Api
   dotnet user-secrets set "Azure:Storage:BlobServiceUri" "https://<account>.blob.core.windows.net" --project src\RunnersDiary.Api
   dotnet user-secrets set "Azure:Storage:GpxContainer" "<container>" --project src\RunnersDiary.Api
   ```

   Configure a stable, private guest-token signing key and the Entra External ID issuer/audience as well:

   ```powershell
   $guestSigningKey = [Convert]::ToBase64String([Security.Cryptography.RandomNumberGenerator]::GetBytes(32))
   dotnet user-secrets set "Auth:Guest:SigningKey" $guestSigningKey --project src\RunnersDiary.Api
   dotnet user-secrets set "Auth:Entra:Authority" "<External ID authority URL>" --project src\RunnersDiary.Api
   dotnet user-secrets set "Auth:Entra:Audience" "<API application ID>" --project src\RunnersDiary.Api
   ```

   Do not print or commit the signing key. Without a local guest key, Development creates a per-process ephemeral key, so guest sessions stop working when the API restarts. These settings identify external resources. API SQL connections use SqlClient `Active Directory Default`; Blob access and readiness checks use `DefaultAzureCredential`, all with the developer's Entra identity. Grant that identity only the database access required for the application and read-only readiness probe. Do not use production resources or check local settings into git.

2. Sign in with an Entra credential available to `DefaultAzureCredential`.
3. Apply the checked-in EF migration to the isolated, non-production SQL database:

   ```powershell
   dotnet tool restore
   dotnet ef database update --project src\RunnersDiary.Infrastructure --startup-project src\RunnersDiary.Api
   ```

   Migrations create the schema and seed the run-type catalog. They do not provision Azure resources. Review the target database before applying migrations; never point this command at production.
4. Install frontend dependencies once, then from the repository root start the API and React development server:

   ```powershell
   npm --prefix web ci
   dotnet run --project src\RunnersDiary.AppHost
   ```

   Aspire runs only the API and frontend. It does not create, start, or modify Azure resources or local containers. The `/health/ready` probe checks access to the configured Azure SQL database and Blob container; it never creates data or resources.

The guest bootstrap endpoint is `POST /api/auth/guest`; it is intentionally the only application endpoint that does not require an existing bearer token. It is limited to five requests per observed client IP every 15 minutes. All run endpoints require a valid Entra or guest JWT and scope reads and writes to that token's user. Run date-range queries require inclusive UTC `from` and `to` dates. The frontend client exports `setApiAccessToken(token)` for attaching the current bearer token.

### Calendar UI and Entra sign-in (M3)

- Open `/sign-in` and choose **Try as guest** to create an isolated demo diary. No frontend Entra configuration is needed for guests; the running API and its configured development database are required.
- For real sign-in, copy `web/.env.example` to the ignored `web/.env.local`. Set the public `VITE_ENTRA_CLIENT_ID` (SPA application ID), `VITE_ENTRA_AUTHORITY` (HTTPS External ID authority), and `VITE_ENTRA_API_SCOPE` (the delegated API scope exposed by the API registration). Grant the SPA permission to that scope and align the API audience/issuer configuration. Restart Vite after changing these values.
- Register `<frontend-origin>/auth-callback.html` as a **Single-page application** redirect URI in Entra for each allowed local/hosted origin. MSAL 5 uses this dedicated redirect-bridge page for popup sign-in and silent renewal; serve the built HTML entry, not the application fallback, at this path.
- These identifiers are public build-time configuration, **not secrets**. Do not put client secrets, API signing keys, connection strings, or map subscription keys in `VITE_*` variables.
- Tokens/session and Query cache are memory-only. Refreshing the page requires signing in again; signing out clears cached run data and MSAL's memory cache. Guest expiry or an API 401 returns to sign-in. MSAL silently acquires fresh API tokens for real users; interaction-required failures also return to sign-in.
- `/diary/YYYY/MM` is bookmarkable. Dates/times are UTC; complete Monday–Sunday weeks are queried for completed-only totals, including adjacent-month days. Cross-training/rest distance is excluded from running distance.
- The calendar uses arrow keys, Home/End, Page Up/Down (Shift for year), and Enter/Space to open a day. Click the month heading for the month list. On narrow screens the grid scrolls horizontally; navigation, forms, and panels fit the viewport.
- Vite proxies `/api` to Aspire's `API_HTTPS`/`API_HTTP` endpoint. A production host must route same-origin `/api` to the API and provide SPA fallback for `/diary/*` while serving `/auth-callback.html` directly.

When hosting behind a reverse proxy, configure `Networking:ForwardedHeaders:KnownProxies` and/or `Networking:ForwardedHeaders:KnownNetworks` with only the trusted proxy addresses or CIDRs, and set `Networking:ForwardedHeaders:ForwardLimit` to the number of trusted forwarding hops. Forwarded headers from untrusted sources are ignored; do not trust arbitrary `X-Forwarded-For` values.

### Maps and GPX (M4)

- Configure the API's `Azure:Maps:ClientId` with the **existing Maps account client
  ID**, not a subscription key. `DefaultAzureCredential`'s local identity needs the
  existing **Azure Maps Data Reader** role on that account. No Maps credential or
  configuration is required in Vite. See [local setup and limitations](docs/architecture.md#m4--maps-and-gpx)
  and [ADR 0004](docs/adr/0004-maps-gpx-private-imports.md).
- Save a run, then edit it or open details to upload/replace a GPX (20 MiB maximum).
  Choose planned course or recorded track. The map shows start/finish and the
  elevation profile; the original is downloadable only through the authenticated
  API. Guests can hold ten active tracks including seeded demo tracks.
- Upload never changes run fields. For completed recorded tracks, explicitly
  **Apply suggested metrics and start location**, review, then **Save run**. Planned
  courses never offer autofill. Track deletion leaves run metrics untouched.
- The additive M4 migration is checked in but must be reviewed/applied separately
  against an isolated database. Startup does not create containers or apply schema
  changes. GPX storage must already be private with data read/write/delete access.
- The fictional `seed/demo/synthetic-silesia.gpx` is embedded for configured demo
  seeding. Live Maps/RBAC, SQL concurrency, and storage integration checks require
  a separately authorized isolated environment; offline tests do not contact Azure.

## Build and test

```powershell
dotnet tool restore
dotnet restore RunnersDiary.sln
dotnet build RunnersDiary.sln --no-restore
dotnet test RunnersDiary.sln --no-build
npm --prefix web ci
npm --prefix web run typecheck
npm --prefix web run test
npm --prefix web run lint
npm --prefix web run build
```

The generated TypeScript API types are based on the API's development OpenAPI document. After building the API, `npm run generate:api` starts the API locally on `http://127.0.0.1:5080`, refreshes the generated types, and stops it. The generator does not require Azure credentials.

## Dependency rationale

- `Aspire.Hosting.JavaScript` lets Aspire orchestrate the Vite server without a container; `Microsoft.AspNetCore.OpenApi` is the API contract source.
- `Azure.Identity`, `Azure.Storage.Blobs`, and `Microsoft.Data.SqlClient` support Entra-authenticated, read-only Azure readiness checks; none provisions or modifies resources.
- `openapi-typescript` and `openapi-fetch` generate and consume typed API contracts instead of duplicating them in the frontend.
- React Router and TanStack Query are the routing and server-state libraries selected in the project brief; Vitest and Testing Library cover the web shell.
- The direct `System.Security.Cryptography.Pkcs` reference pins an Azure SDK/test-host transitive dependency to a patched 9.x version because warnings are treated as errors.
- The npm `minimatch` override pins the OpenAPI generator's vulnerable transitive version to a patched compatible release.
- MSAL Browser/React provide Entra authorization-code/PKCE popup sign-in and silent API token acquisition without browser secrets.
- React Hook Form, Zod, and their resolver provide typed local form state and validation; form values map into the existing generated `RunInput`, not a parallel API contract.
- Radix Dialog supplies focus trapping, Escape handling, and ARIA dialog primitives. It is the M3 component-library alternative to shadcn/Tailwind, retaining the existing lightweight CSS.
- Testing Library user-event exercises realistic keyboard and form interactions alongside Vitest.
- EF Core SQL Server with NetTopologySuite stores run data and WGS84 locations in Azure SQL; the test suite uses the EF in-memory provider without requiring Azure credentials.
- ASP.NET Core JWT bearer authentication validates Entra External ID tokens and app-issued, short-lived guest tokens. The guest signing key is supplied through local user secrets or deployment secret references.
