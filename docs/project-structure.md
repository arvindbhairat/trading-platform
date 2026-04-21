# Project Structure Notes

## Purpose

This repo is intended to be a **monorepo** with three deployable applications and a small set of shared packages.
The canonical structure definition is in [`repo-structure.md`](./repo-structure.md); this file is a practical, high-level map of what exists in the workspace and where new code should go.

## Top-level layout

```
apps/
  web/               Next.js portal (user + admin UI)
  api/               ASP.NET Core backend API
  worker/            .NET background jobs runner
packages/
  shared-types/      Cross-app DTOs and shared domain contracts
  ui/                Shared UI primitives and design tokens
  config/            Shared config loading + env validation
  testing/           Shared test helpers and fixtures
infra/
  docker/            Local dev containers / compose
  azure/             Azure deployment + pipeline support files
docs/                Canonical product + architecture documentation
.claude/skills/      Agent task-focused execution aids (derived from docs)
```

## `apps/web` (portal)

`apps/web` is the thin UI tier. Keep business logic in the backend; the web app should focus on composing pages, calling the API, and rendering charts/widgets.

Preferred internal layout:

```
apps/web/src/
  app/          Route segments + layouts (Next.js App Router)
  features/     Feature modules (auth, charts, backtests, admin-*)
  components/   Shared view components
  lib/          API clients, auth helpers, websocket helpers, utilities
```

## `apps/api` (backend API)

`apps/api` is the authoritative server-side layer for auth, validation, rules, and integrations.

Preferred internal layout:

```
apps/api/app/
  api/            Route handlers + transport models
  domain/         Core business entities and rules
  services/       Orchestration and application services
  repositories/   MongoDB + SQL Server access layers
  integrations/   Provider adapters (FYERS, Telegram, OAuth, etc.)
  config/         Config loading, sys_config readers, App Config integration
  admin/          Admin-only workflows
    imports/      CSV parsing + reconciliation for imports/sync
  risk/           Portfolio risk rules, sizing, validation
  jobs/           Background workflow definitions shared with worker where needed
```

Notes:
- Keep **SQL Server historical access** separate from **MongoDB operational repositories**.
- Keep **provider adapters** out of domain models.

## `apps/worker` (background jobs)

`apps/worker` runs scheduled and long-running background workflows (sync, notifications, runners).

Preferred internal layout:

```
apps/worker/worker/
  jobs/       Backtest, signal, notification, and sync jobs
  runners/    Scheduling and execution entry points
  lib/        Shared worker utilities + observability helpers
  config/     Bootstrap config and runtime setting access
```

## Shared packages

`packages/` holds small, reusable modules shared by `apps/*`.

Guidance:
- Prefer **clear ownership boundaries**: UI components in `packages/ui`, DTOs/contracts in `packages/shared-types`, config loading in `packages/config`.
- Avoid creating “misc utils” packages; place utilities close to the app/feature unless they’re truly cross-cutting.

