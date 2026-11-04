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

   Do not print or commit the signing key. Without a local guest key, Development creates a per-process ephemeral key, so guest sessions stop working when the API restarts.    These settings identify external resources. API SQL connections use SqlClient `Active Directory Default`; Blob access and readiness checks use `DefaultAzureCredential`, all with the developer's Entra identity. Grant that identity only the database access required for the application and read-only readiness probe. Do not use production resources or check local settings into git.

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

When hosting behind a reverse proxy, configure `Networking:ForwardedHeaders:KnownProxies` and/or `Networking:ForwardedHeaders:KnownNetworks` with only the trusted proxy addresses or CIDRs, and set `Networking:ForwardedHeaders:ForwardLimit` to the number of trusted forwarding hops. Forwarded headers from untrusted sources are ignored; do not trust arbitrary `X-Forwarded-For` values.

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
- EF Core SQL Server with NetTopologySuite stores run data and WGS84 locations in Azure SQL; the test suite uses the EF in-memory provider without requiring Azure credentials.
- ASP.NET Core JWT bearer authentication validates Entra External ID tokens and app-issued, short-lived guest tokens. The guest signing key is supplied through local user secrets or deployment secret references.
