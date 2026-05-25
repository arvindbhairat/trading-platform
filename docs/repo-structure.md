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
- `packages/domain`
  - shared domain model types (UserDocument, AuditEventDocument, BacktestModels, Signal models, etc.) and repository interfaces
- `packages/storage`
  - shared storage implementations (MongoDB repositories, PostgreSQL repositories, DI registration extensions)
- `packages/historical`
  - shared historical OHLCV services (SymbolTableMappingService, TimeframeService)
- `packages/signals`
  - shared signal evaluation and backtesting engine used by both API and Worker
- `packages/notifications`
  - shared notification services (Telegram bot, template builder, notification writer)
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

The API is a thin presentation layer. Domain models, storage implementations, and shared services live in `packages/`.

Inside `apps/api`, prefer:

- `Api/`
  - route handlers and transport models
- `Auth/`
  - OAuth endpoints, JWT service, CSRF middleware
- `Sessions/`
  - session middleware
- `Pld/`
  - WebSocket connection manager, endpoints
- `PhaseEnforcement/`
  - phase middleware
- `Admin/`
  - admin endpoints, imports, impersonation middleware, phase gate
- `Notifications/`, `Backtesting/`, `Historical/`, `Universe/`, `Execution/`, `Portfolio/`, `Risk/`, `Signals/`, `Users/`, `Fyers/`, `Audit/`, `TelegramBot/`, `PrivacyRequest/`, `DataBreach/`, `Push/`, `LedgerWriters/`, `SysConfig/`
  - endpoint-only folders
- `Observability/`
  - OpenTelemetry bootstrap

For domain entities, repository implementations, and shared services, prefer placing code in the appropriate `packages/` project instead.

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
