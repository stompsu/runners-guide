# Runners Diary — Copilot instructions

## Project summary

Runners Diary is a private, multi-user running diary for planning and tracking runs, races, training load, and insights. Guest users receive isolated demo accounts that expire after 24 hours. The full product scope, constraints, and milestone acceptance criteria are in [docs/PROJECT_BRIEF.md](../docs/PROJECT_BRIEF.md); follow it when making project decisions.

## Repository status and proposed layout

The repository currently contains only `docs/PROJECT_BRIEF.md` and the Phase 0 customization files. The following is the proposed layout for implementation; directories and commands do not exist until their milestone creates them.

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
    api/
      generated/
infra/
tests/
  RunnersDiary.UnitTests/
  RunnersDiary.IntegrationTests/
  web/
```

Keep domain logic such as training-load calculations and GPX metrics pure and unit-tested. Keep the OpenAPI contract and generated TypeScript client aligned. Place synthetic demo data and GPX fixtures under `seed/demo/`.

## Build, test, and run commands

There is no solution, frontend package manifest, or test project yet, so there are no verified build, test, or run commands today. M1 is expected to establish the Aspire AppHost and frontend commands. After those files exist, inspect their manifests and README, use the commands they define, and update this section with the verified commands. Do not treat anticipated commands as runnable before M1.

## Conventions and safety

- Backend: C#/.NET 10, ASP.NET Core Minimal APIs, vertical slices, EF Core, nullable reference types, warnings as errors, analyzers enabled.
- Frontend: React + TypeScript with strict mode; use the generated API client rather than duplicating API contracts.
- Require authentication on every application endpoint and enforce that every caller owns the data they access.
- Validate uploads for size and content; parse XML safely without DTDs or external entities. Never trust file contents.
- Store values in SI units and format them for the UI. Keep user-facing strings structured for localization.
- Do not commit secrets, keys, or connection strings. Do not add dependencies without a one-line justification.
- Keep Azure usage cost-conscious. Never expose Azure Maps subscription keys in browser code.
- AI summaries may interpret only deterministic, precomputed metrics; they must not invent data.

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
