# Repo Structure

## Goal

Use a clear monorepo layout so Claude can add features without scattering code or mixing concerns.
This file defines preferred implementation structure; product behavior still belongs in [requirements-spec.md](./requirements-spec.md).

## Preferred Structure

- `apps/web`
  - Next.js unified portal for users and admins
- `apps/api`
  - ASP.NET Core backend for auth, strategies, admin operations, data access, and integrations
- `apps/worker`
  - .NET background job runner for backtests, live strategy execution, notifications, and sync jobs
- `packages/shared-types`
  - cross-application request, response, and domain DTO definitions
- `packages/ui`
  - shared portal components, design tokens, and admin/user layout primitives
- `packages/config`
  - shared config loaders, Azure App Configuration integration, environment validation, and constants
- `packages/testing`
  - test helpers, fixtures, mocks, and integration utilities
- `infra/docker`
  - local containers and compose files
- `infra/azure`
  - Azure deployment definitions, environment templates, and pipeline support files
- `docs`
  - architecture, product, risk, and implementation guidance
- `.claude/skills`
  - Claude-specific focused build instructions

## API Internal Layout

Inside `apps/api`, prefer:

- `app/api`
  - route handlers and transport models
- `app/domain`
  - core business entities and rules
- `app/services`
  - orchestration and application services
- `app/repositories`
  - MongoDB and PostgreSQL access layers
- `app/integrations`
  - Fyers, Telegram, OAuth, and other provider adapters
- `app/config`
  - configuration loading, `sys_config` readers, and App Configuration integration
- `app/admin`
  - admin approval, universe sync, and operational actions
- `app/admin/imports`
  - CSV parsing, upload summary generation, and sync reconciliation logic
- `app/risk`
  - portfolio risk rules, sizing, and validation
- `app/jobs`
  - background workflows and schedulers

## Web Internal Layout

Inside `apps/web`, prefer:

- `src/app`
  - route segments and layouts
- `src/features`
  - feature modules such as auth, charts, strategy-builder, backtests, admin-users, admin-universe
- `src/components`
  - shared view components
- `src/lib`
  - API clients, auth helpers, websocket helpers, and utilities

## Worker Internal Layout

Inside `apps/worker`, prefer:

- `worker/jobs`
  - backtest, signal, notification, and sync jobs
- `worker/runners`
  - scheduling and execution entry points
- `worker/lib`
  - shared worker utilities and observability helpers
- `worker/config`
  - bootstrap config, shared config loading, and runtime setting access

## Folder Rules

- keep admin flows in distinct modules even when they share portal infrastructure with user flows
- keep PostgreSQL historical access code separate from MongoDB operational repositories
- keep provider adapters out of domain models
- keep CSV parsing and Nifty 500 sync logic isolated and testable
- keep raw symbols separate from sanitized SQL table names in the symbol-master model
- keep bootstrap config separate from MongoDB-backed runtime settings
- prefer feature folders over generic utility dumping grounds
