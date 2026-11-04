# Runners Diary — Copilot instructions

## Project summary

Runners Diary is a private, multi-user running diary for planning and tracking runs, races, training load, and insights. Guest users receive isolated demo accounts that expire after 24 hours. The full product scope, constraints, and milestone acceptance criteria are in [docs/PROJECT_BRIEF.md](../docs/PROJECT_BRIEF.md); follow it when making project decisions.

## Repository status and proposed layout

The repository currently contains the M1 Aspire skeleton, M2 domain and Runs API, and M3 calendar UI. Maps/GPX, insights, AI, and deployment features remain for later milestones.

```text
.github/
  agents/
  copilot-instructions.md
docs/
  PROJECT_BRIEF.md
  architecture.md
  adr/
seed/
  demo/
src/
  RunnersDiary.Api/
  RunnersDiary.Domain/
  RunnersDiary.Infrastructure/
  RunnersDiary.AppHost/
  RunnersDiary.ServiceDefaults/
web/
  src/
    auth/
    diary/
    api/
      generated/
infra/
tests/
  RunnersDiary.Api.Tests/
```

Keep domain logic such as training-load calculations and GPX metrics pure and unit-tested. Keep the OpenAPI contract and generated TypeScript client aligned. Place synthetic demo data and GPX fixtures under `seed/demo/`.

## Build, test, and run commands

From the repository root:

```powershell
dotnet restore RunnersDiary.sln
dotnet build RunnersDiary.sln --no-restore
dotnet test RunnersDiary.sln --no-build
npm --prefix web ci
npm --prefix web run generate:api
npm --prefix web run typecheck
npm --prefix web run test
npm --prefix web run lint
npm --prefix web run build
dotnet run --project src\RunnersDiary.AppHost
```

The API types are generated from its Development OpenAPI document with `npm --prefix web run generate:api`; first build the API with `dotnet build RunnersDiary.sln`. Generation does not require Azure credentials. Local Aspire development requires .NET 10 SDK and Node.js 22.13 or Node.js 24 LTS on `PATH`, but not Docker. Azure readiness checks require separately provisioned development SQL and Blob resources and Entra permissions; see [README.md](../README.md). CI does not connect to Azure or start containers.

## Conventions and safety

- Backend: C#/.NET 10, ASP.NET Core Minimal APIs, vertical slices, EF Core, nullable reference types, warnings as errors, analyzers enabled.
- Frontend: React + TypeScript with strict mode; use the generated API client rather than duplicating API contracts.
- Require authentication on every application endpoint and enforce that every caller owns the data they access.
- Validate uploads for size and content; parse XML safely without DTDs or external entities. Never trust file contents.
- Store values in SI units and format them for the UI. Keep user-facing strings structured for localization.
- Do not commit secrets, keys, or connection strings. Do not add dependencies without a one-line justification.
- Keep Azure usage cost-conscious. Never expose Azure Maps subscription keys in browser code.
- AI summaries may interpret only deterministic, precomputed metrics; they must not invent data.
- Run the API and frontend locally with Aspire; do not assume Docker or local containers are available. Use the separately provisioned, non-production Azure development resources and the developer's Entra identity. Keep integration-test data isolated from shared development data.

## How we work

- Follow the phases in the project brief; do not skip ahead. Before each milestone, ask `architect` for a plan. After implementation, have `qa-tester` run the tests and `reviewer` review the diff, then stop for approval.
- Make small, reviewable changes and keep the build green. Ask when requirements are ambiguous and state assumptions when proceeding.
- Check current documentation for Azure Maps, Microsoft Foundry, Microsoft Agent Framework, and Aspire APIs because these change frequently.
- Keep this file and `docs/` current as decisions change.
- Never run `azd up`, deploy, or change cloud resources without the user's explicit confirmation. For any proposed deployment, show the plan and estimated monthly cost first.
- Commit once per approved milestone with a clear message.

## Agent delegation

- `architect`: milestone plans, architecture docs, ADRs, and cross-cutting decisions.
- `backend-dotnet`: ASP.NET Core APIs, EF Core, authorization, validation, backend domain logic, and backend tests.
- `frontend-react`: React UI, routing, forms, calendar, dashboard, accessibility, and responsive behavior.
- `maps-gpx`: GPX and geospatial processing, map integration, GeoJSON, token flow, and elevation profiles.
- `ai-insights`: metrics-to-narrative pipeline, agent tools and prompts, caching, evaluation, and cost limits.
- `azure-infra`: Bicep, azd, Azure resources, identities, observability, and deployment workflows; no deployment without explicit confirmation.
- `qa-tester`: tests, test fixtures, and reproducible failure reports.
- `reviewer`: read-only correctness and security review after each milestone.
