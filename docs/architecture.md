# Architecture

## M1 proposal — skeleton

Status: proposed for M1; subject to the open questions below.

- Use a .NET 10 solution with a C# Aspire AppHost, API, and ServiceDefaults project. Aspire orchestrates the API and Vite development server; expose the Vite endpoint for local browser access. Azure SQL and Blob Storage remain external, already-provisioned resources. Do not add local containers or provisioning/deployment steps.
- Keep the API contract in ASP.NET Core Minimal APIs and publish its OpenAPI document. Generate TypeScript contract types from that document with `openapi-typescript` and use `openapi-fetch` as the typed HTTP client. Generated files are checked in and regenerated/verified in CI.
- The React + TypeScript + Vite shell uses React Router. M1 adds no run-data behavior; authentication and user-facing application routes remain subject to the M2 auth milestone.
- Local Azure configuration is supplied outside source control. Use `DefaultAzureCredential` with the developer's Entra identity; the API may perform read-only SQL and Blob connectivity checks against configured resources. It must not create resources, containers, schemas, or data as a side effect of startup or health checks.
- CI restores, builds, and tests .NET projects and installs, tests, and builds the web app without Azure credentials or containers.

### M1 decisions and remaining setup

- The only unauthenticated API routes in M1 are Development-only infrastructure probes (`/health/live`, `/health/ready`) and the development OpenAPI document. Liveness returns a fixed status and readiness only returns a standard health status; neither returns user data or diagnostic details. These routes are not mapped outside Development. Add application authentication before exposing application API routes in M2.
- Readiness verifies Azure SQL with a read-only `SELECT 1` and Blob Storage with a read-only container-properties request. It does not create resources, containers, schemas, or data. The AppHost waits only for liveness, so a developer can run the app shell without Azure configuration; the readiness probe reports unhealthy until dev resource settings and permissions are configured.
- The developer supplies the non-secret Azure resource identifiers with API user secrets, then authenticates with `DefaultAzureCredential`. Actual resource names and Entra permissions remain developer setup inputs; they are not required for CI.

See [ADR 0001](adr/0001-m1-skeleton-orchestration-and-api-client.md).
