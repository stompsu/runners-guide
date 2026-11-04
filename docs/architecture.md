# Architecture

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
