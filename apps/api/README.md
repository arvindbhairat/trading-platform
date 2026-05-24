## apps/api

ASP.NET Core backend for auth, strategies, admin operations, data access, and integrations.

Preferred internal layout (see `docs/repo-structure.md`) lives under `app/`:
- `app/api`: route handlers and transport models
- `app/domain`: core business entities and rules
- `app/services`: orchestration and application services
- `app/repositories`: MongoDB + PostgreSQL access layers
- `app/integrations`: provider adapters (FYERS, Telegram, OAuth, etc.)
- `app/config`: config loading and runtime setting access
- `app/admin`: admin-only workflows (imports under `app/admin/imports`)
- `app/risk`: portfolio risk rules, sizing, validation
- `app/jobs`: background workflows and schedulers

