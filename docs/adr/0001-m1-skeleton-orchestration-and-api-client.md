# ADR 0001: M1 skeleton orchestration and API client

- Status: Proposed
- Date: 2026-11-04

## Context

M1 needs one local entry point for the .NET API and React/Vite shell, a generated TypeScript client, and optional connectivity to separately provisioned Azure development resources. The repository has no application code yet. Local development must not depend on containers or cloud provisioning, and API contracts should not be duplicated by hand.

## Decision

For M1, propose:

1. A .NET 10 solution with a C# Aspire AppHost, ASP.NET Core Minimal API, and Aspire ServiceDefaults. Use the `Aspire.Hosting.JavaScript` Vite integration (`AddViteApp`) to run the React/Vite development server alongside the API.
2. ASP.NET Core OpenAPI as the API document source. Generate checked-in TypeScript types with `openapi-typescript`, and make requests through `openapi-fetch`; CI regenerates or verifies the generated output against the API document.
3. Treat Azure SQL and Blob Storage as external dependencies, not Aspire-managed resources. Configure endpoints outside git, authenticate locally with `DefaultAzureCredential` and the developer's Entra identity, and make any M1 connectivity checks read-only. Do not require Azure credentials for build or unit tests.
4. Keep M1 to a routed React shell and health/connectivity surface; defer domain behavior and application authentication to M2. Map only Development-only, fixed-response infrastructure probes. Liveness contains no diagnostic data; readiness returns only the health status and performs read-only Azure SQL and Blob checks.

This keeps orchestration aligned with the brief, avoids local containers and hand-maintained API types, and leaves relational persistence as the planned M2 data foundation.

## Consequences

- Aspire requires the .NET SDK, Node.js/npm, and the JavaScript hosting integration, but should not start SQL or storage containers.
- The API contract is generated from OpenAPI; generated files must be refreshed whenever the document changes and checked by CI.
- Local cloud connectivity requires pre-provisioned development resources, correct Entra permissions, and local endpoint configuration. Build/test must remain independent of those prerequisites.
- Exact Aspire package versions should be pinned to a mutually compatible stable release during implementation rather than copied from a floating example.

## Remaining setup

The brief requires authentication for all application endpoints. M1's local infrastructure-only probes are not application endpoints and are not mapped outside Development. Application authentication must be implemented before application API routes are exposed in M2. Developers must separately configure their already-provisioned development SQL and Blob resource identifiers and Entra permissions; CI does not require them.
