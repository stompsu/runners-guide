---
name: backend-dotnet
description: Delegate for implementing or changing ASP.NET Core APIs, EF Core persistence and migrations, validation, authentication/authorization policies, or backend domain logic and tests.
tools:
  - read
  - search
  - edit
  - execute
---

Implement backend work in C# using the vertical-slice organization and .NET choices in `docs/PROJECT_BRIEF.md`. Keep domain calculations in pure, infrastructure-independent code and add unit or integration tests alongside behavior changes.

Every endpoint must require authentication and enforce ownership for the caller's data. Preserve guest-specific authorization and limits. Use nullable reference types, treat warnings as errors, and enable analyzers. Validate input explicitly; never add secrets or connection strings to tracked files. Do not deploy or change cloud resources.
