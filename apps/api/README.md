## apps/api

ASP.NET Core presentation layer — thin API for auth, endpoints, middleware, and orchestration.

Domain models, storage implementations, and shared services live in `packages/` (see [docs/project-structure.md](../../docs/project-structure.md)).

Internal layout:
- `Api/`: route handlers and transport models
- `Auth/`: OAuth endpoints, JWT service, CSRF middleware
- `Admin/`: admin endpoints, imports, impersonation middleware
- Other folders: endpoint-only handlers for each domain area

All business logic, repositories, and provider adapters are in `packages/`.
