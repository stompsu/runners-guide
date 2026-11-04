# ADR 0002: M2 domain, Runs API, and guest identity

- Status: Accepted
- Date: 2026-11-04

## Context

M2 adds private run data and guest demo accounts to the M1 skeleton. The product requires every data endpoint to be authenticated, per-user isolation, Entra External ID for real users, a self-service guest flow, and expiry-based deletion. Run values are stored in SI units and Azure SQL remains an externally provisioned dependency.

## Decisions

1. Keep run entities and validation in `RunnersDiary.Domain`; EF Core SQL Server, migrations, relational mappings, demo seeding, and guest cleanup live in `RunnersDiary.Infrastructure`. Locations use an SRID 4326 point stored as SQL Server `geography`. Seeded run-type codes are stable API/domain identifiers. Race details and GPX metadata are one-to-one related records.
2. Validate real-user JWTs against the configured Entra External ID authority and audience. Guest JWTs use HMAC-SHA256 with a minimum 256-bit secret, the configured issuer/audience, a `guest` role, and the guest user's 24-hour expiry. Production requires both authentication configurations; a development-only ephemeral guest key supports local runs without adding secrets to source control.
3. `POST /api/auth/guest` is the sole unauthenticated application bootstrap exception: it creates a private guest dataset and returns only the short-lived token and expiry. It is rate-limited to five creations per observed IP per 15 minutes. Every run operation requires authentication and scopes database queries/mutations to the subject of the validated token. A foreign or missing run ID returns the same 404 response.
4. Demo data is a JSON template in `seed/demo/`; the seeder expands its week schedule and race offsets relative to the current UTC date. Each guest receives a separate 24-hour user ID and rows. An hourly cleanup deletes related GPX blobs before cascading database deletion; a failed blob delete retains the user's rows for retry.
5. Date queries require inclusive UTC dates with a maximum span of 367 days. Migrations are checked in but never applied automatically at API startup. Developers must explicitly target an isolated, non-production database.

## Consequences

- Local run data and guest creation require the configured development SQL database and an applied migration. Health checks and API startup do not provision resources or apply schemas.
- A stable guest signing key is needed across API restarts. Production deployments must source it from a secret store and configure Entra External ID.
- The bootstrap rate limiter is process-local and partitions by the remote IP observed by ASP.NET Core. Forwarded client IPs are honored only when the proxy is explicitly configured as trusted. A multi-instance deployment must use a distributed rate-limit store or equivalent ingress policy before scaling guest creation across replicas.
- The cleanup worker logs transient database, token-credential, and blob failures and retries on its next hourly pass; deletion remains idempotent.
- GPX parsing, upload validation, maps, and frontend sign-in remain in later milestones.

## Alternatives considered

- Requiring a token for guest bootstrap would make first-time guest creation impossible. The narrow token-only endpoint is therefore the smallest exception consistent with the no-anonymous-data requirement.
- Automatically applying migrations on startup risks unexpected schema changes to shared or production databases. Migrations remain an explicit operator action.
