# Project Structure

## Purpose

This document is the practical, navigable map of the repository — what exists today, what is planned, and where every piece of new code belongs. It is aimed at both human developers and AI agents implementing tasks.

The **canonical layout rules** live in [`repo-structure.md`](./repo-structure.md). This file extends those rules with real paths, notes on what has been built, and navigation guidance.

---

## Navigation Quick Reference

This map shows which document to consult for each concern during task execution:

| Concern | Document |
|---|---|
| **Current task to execute** | `execution_plan/status.json` → `current_task_id` |
| **Task definition (REQ, dependencies)** | `execution_plan/execution_plan.md` |
| **Guardrails (must never violate)** | `CLAUDE.md` / `AGENTS.md` |
| **Canonical names** | `docs/terminology.md` |
| **Product requirements (REQ-IDs)** | `docs/requirements-spec.md` |
| **Delivery sequence** | `docs/implementation-roadmap.md` |
| **Architecture + flows** | `docs/system-architecture.md` |
| **Where files go in this repo** | THIS FILE (`docs/project-structure.md`) |
| **Canonical layout rules** | `docs/repo-structure.md` |
| **Task log output** | `execution_plan/task_logs/{TIMESTAMP}__{TASK_ID}.md` |

## How to read this document

**If you are an AI agent executing a task from `execution_plan/agent.md`:**

1. Read `CLAUDE.md` (or `AGENTS.md`) at the root for non-negotiable guardrails.
2. Read `execution_plan/status.json` to identify the next task.
3. Read the task's **Touches** field in `execution_plan/execution_plan.md` to know which components to change.
4. Use this file to locate the exact folders where your changes belong.
5. Use `docs/terminology.md` before naming anything new.
6. Read the matching skill file at `.claude/skills/{domain}.md` before writing code.
7. Write your task log to `execution_plan/task_logs/`.

**If you are a human developer starting a new feature:**

1. Read `docs/README.md` for the document map.
2. Read `docs/requirements-spec.md` for product requirements.
3. Check `docs/implementation-roadmap.md` for which phase owns the feature.
4. Return here to find where to put the code.

## Execution Plan Integration

The execution plan (`execution_plan/`) drives incremental delivery. Each task has a **Touches** field that maps to folders in this repo:

| Touches value | Maps to folder(s) |
|---|---|
| `apps/api` | `apps/api/` |
| `apps/web` | `apps/web/` |
| `apps/worker` | `apps/worker/` |
| `packages/*` | `packages/` |
| `infra/` | `infra/` |
| `docs/` | `docs/` |
| `.github/workflows/` | `.github/workflows/` |
| `tests/` | `tests/` |

When your task's **Touches** includes any of these, use the "Where to put new code" table below to locate the exact subfolder.

---

## Top-level layout

```
signalstack/                  ← repo root
├── CLAUDE.md                 ← AI agent config for Claude / Cowork (non-negotiable guardrails)
├── AGENTS.md                 ← AI agent config for Codex / OpenAI agents (same guardrails)
├── RUNBOOK.md                ← Single-line pointer: tells agents to read execution_plan/agent.md
├── README.md                 ← Human-readable project intro
├── SignalStack.sln           ← .NET solution file (includes api + worker + their tests)
├── Directory.Build.props     ← Shared MSBuild properties for all .NET projects
├── NuGet.Config              ← NuGet package source configuration
├── global.json               ← .NET SDK version pin
├── package.json              ← Root Node package (workspace config for apps/web)
├── package-lock.json         ← Lockfile for root Node workspace
│
├── apps/                     ← Three deployable applications (see below)
├── tests/                    ← Automated test projects, mirroring apps/ structure
├── packages/                 ← Shared packages (PLANNED — not yet scaffolded)
├── infra/                    ← Infrastructure as code and local dev tooling
├── design_system/            ← Brand tokens, CSS variables, UI kit reference components
├── docs/                     ← All canonical documentation (architecture, requirements, ADRs)
├── execution_plan/           ← AI-agent task pipeline, status tracker, and task logs
└── .claude/skills/           ← Focused build-instructions for Claude agents (derived from docs/)
```

---

## `apps/` — Deployable applications

### `apps/api/` — ASP.NET Core backend (C#)

The authoritative server-side layer: authentication, domain rules, validation, API surface, and all provider integrations. Keeps business logic out of the frontend.

**Currently implemented:**
```
apps/api/
├── SignalStack.Api.csproj         ← Project file
├── Program.cs                     ← Application entry point, DI wiring, middleware
├── appsettings.json               ← Local config (non-secret defaults only)
├── Observability/
│   ├── OtlpTelemetryOptions.cs    ← OTLP exporter configuration model
│   └── TelemetryBootstrapExtensions.cs  ← Registers Serilog + OpenTelemetry on startup
├── Properties/
│   └── launchSettings.json        ← Local launch profiles
└── README.md                      ← App-level developer notes
```

**Planned internal layout (add new code here):**
```
apps/api/
├── Api/                    ← Route handlers and transport (request/response) models
├── Domain/                 ← Core business entities, value objects, domain rules
├── Services/               ← Orchestration and application services
├── Repositories/
│   ├── Mongo/              ← MongoDB access (users, signals, portfolio, notifications…)
│   └── Sql/                ← PostgreSQL access (historical OHLCV only)
├── Integrations/
│   ├── Fyers/              ← FYERS adapter (token, quotes, account data, widget signing)
│   ├── Telegram/           ← Telegram bot integration
│   └── OAuth/              ← Google / Microsoft / Meta OAuth adapters
├── Config/                 ← Config loading, sys_config readers, App Configuration
├── Admin/
│   └── Imports/            ← CSV parsing, Nifty 500 universe sync reconciliation
├── Risk/                   ← Portfolio risk rules, sizing models, validation
└── Jobs/                   ← Background workflow definitions shared with worker
```

**Key rules for `apps/api/`:**
- PostgreSQL historical access and MongoDB operational access must live in separate repository classes.
- Provider adapters (FYERS, Telegram, OAuth) must not leak into domain models.
- The backend must never call the FYERS order-placement REST API directly.
- All business logic, validation, and permission checks belong here, not in `apps/web/`.

---

### `apps/web/` — Next.js portal (TypeScript)

The thin UI tier. Composes pages, calls the API, and renders charts and widgets. Contains no business logic and no risk calculations.

**Currently implemented:**
```
apps/web/
├── package.json               ← Node dependencies (Next.js, TypeScript, etc.)
├── next.config.ts             ← Next.js configuration
├── tsconfig.json              ← TypeScript configuration
├── eslint.config.mjs          ← ESLint rules
├── next-env.d.ts              ← Next.js TypeScript ambient types
├── src/
│   └── app/
│       ├── layout.tsx         ← Root layout (HTML shell, fonts, providers)
│       └── page.tsx           ← Homepage / landing route
└── README.md                  ← App-level developer notes
```

**Planned internal layout (add new code here):**
```
apps/web/src/
├── app/                    ← Next.js App Router: route segments and layouts
│   ├── (user)/             ← User-facing routes (dashboard, charts, signals, portfolio)
│   └── (admin)/            ← Admin-facing routes (universe sync, job controls, user approval)
├── features/               ← Feature modules, one folder per bounded feature
│   ├── auth/               ← OAuth login, FYERS re-auth modal, session management
│   ├── charts/             ← TradingView Lightweight Charts integration + RME panel
│   ├── signals/            ← Signal Subscription builder, scan results, backtest views
│   ├── portfolio/          ← Holdings, positions, PnL, XIRR, equity curve
│   ├── execution/          ← FYERS API Connect widget integration, order modal
│   ├── admin-universe/     ← CSV upload, diff preview, rename resolution
│   ├── admin-users/        ← User approval, FYERS connection status
│   └── admin-jobs/         ← Job controls, operational status widget
├── components/             ← Shared view components (tables, KPI cards, badges, etc.)
└── lib/                    ← API clients, auth helpers, WebSocket helpers, utilities
```

**Key rules for `apps/web/`:**
- Keep this layer thin: no risk calculations, no business logic.
- Historical OHLCV data comes from PostgreSQL via the API — never from FYERS directly in the backend.
- Live quotes and real-time chart data use the logged-in user's FYERS token at display time (browser-tier WebSocket is permitted).
- TradingView fundamental widgets load as iframes using `NSE:{symbol}` format — no platform API call needed.

---

### `apps/worker/` — .NET Background Worker (C#)

Runs all scheduled and long-running background jobs: market data sync, signal execution, notification delivery, account sync, and the singleton lease enforcement that prevents concurrent instances.

**Currently implemented:**
```
apps/worker/
├── SignalStack.Worker.csproj          ← Project file
├── Program.cs                         ← Worker entry point, DI wiring, hosted services
├── appsettings.json                   ← Local config (non-secret defaults only)
├── Configuration/
│   └── WorkerSingletonOptions.cs      ← Config model for the singleton lease settings
├── Hosting/
│   └── WorkerHeartbeatService.cs      ← IHostedService that manages lease acquisition + renewal
├── Observability/
│   ├── OtlpTelemetryOptions.cs        ← OTLP exporter configuration model
│   ├── TelemetryBootstrapExtensions.cs ← Registers Serilog + OpenTelemetry on startup
│   └── WorkerTelemetry.cs             ← Named activity source and meter for worker metrics
├── Singleton/
│   ├── IWorkerInstanceIdentityProvider.cs   ← Abstraction for the worker's unique instance ID
│   ├── IWorkerSingletonCoordinator.cs       ← Abstraction for the lease-acquire/renew loop
│   ├── IWorkerSingletonLeaseBackend.cs      ← Abstraction for the Redis lease store
│   ├── RedisWorkerSingletonLeaseBackend.cs  ← Redis implementation of the lease backend
│   ├── WorkerInstanceIdentityProvider.cs    ← Generates a stable GUID per process lifetime
│   ├── WorkerLeaseAcquireResult.cs          ← Result type for lease acquisition
│   ├── WorkerLeaseRefreshResult.cs          ← Result type for lease renewal
│   ├── WorkerSingletonCoordinator.cs        ← Orchestrates acquire → renew → exit on conflict
│   └── WorkerStartupDecision.cs             ← Outcome enum: Proceed / ExitSingletonViolation
├── Properties/
│   └── AssemblyInfo.cs
└── README.md                          ← App-level developer notes
```

**Planned internal layout (add new code here):**
```
apps/worker/
├── Jobs/                   ← One class per background job
│   ├── DataSync/           ← Daily OHLCV sync to PostgreSQL
│   ├── HistoricDataSeed/   ← Full historical backfill
│   ├── EodSignalRunner/    ← Post-market signal scan
│   ├── LiveMarketScan/     ← LMDS: continuous intraday price + level monitoring
│   ├── LiveAccountScan/    ← LADS: per-user FYERS account sync + intent reconciliation
│   ├── NotificationDelivery/ ← Sole Telegram dispatch; polls notification collection
│   └── SymbolValidityProbe/  ← Daily LTP probe for active universe symbols
├── Runners/                ← Scheduler setup and execution entry points (Hangfire / Quartz)
├── Lib/                    ← Shared worker utilities and observability helpers
└── Config/                 ← Bootstrap config, sys_config runtime setting access
```

**Critical constraint — single instance invariant:**
The Worker must run as **exactly one instance** through Phase A and Phase B. The `Singleton/` subsystem (already implemented) enforces this via a Redis lease. Scale-out is blocked at the infrastructure level (`infra/azure/worker-appservice.bicep`). See `docs/system-architecture.md` § "Worker Service — single-instance invariant" and ADR-0003.

---

## `tests/` — Automated test projects

Mirrors the `apps/` structure. Each test project targets the corresponding application.

```
tests/
├── api/
│   └── SignalStack.Api.Tests/
│       ├── SignalStack.Api.Tests.csproj
│       ├── HealthEndpointsTests.cs        ← Integration tests for /health endpoints
│       ├── OtlpTelemetryOptionsTests.cs   ← Unit tests for OTLP config model validation
│       └── TestLogging.cs                 ← Shared xUnit logging helper
└── worker/
    └── SignalStack.Worker.Tests/
        ├── SignalStack.Worker.Tests.csproj
        ├── OtlpTelemetryOptionsTests.cs               ← Unit tests for OTLP config model
        ├── WorkerSingletonCoordinatorTests.cs          ← Unit tests for singleton lease logic
        └── WorkerSingletonInfrastructureLintTests.cs  ← Structural lint: verifies singleton
                                                           invariant is not accidentally bypassed
```

**Naming convention:** test files mirror the class under test with a `Tests` suffix. Integration tests use `Microsoft.AspNetCore.Mvc.Testing`; unit tests use xUnit with NSubstitute for mocking.

---

## `packages/` — Shared packages

Packages are referenced by `apps/*` projects to avoid duplication.

```
packages/
├── config/
│   └── SignalStack.Configuration/   ← Shared config loaders, Azure App Configuration, env validation,
│                                        LKG cache, bootstrap, logging/redaction, ledger write-lock
├── market-data/
│   └── SignalStack.MarketData/      ← Market Data Provider (MDP) interface contract, DTOs
│                                        (OhlcvRecord, QuoteRecord), provider type enum, and
│                                        provider-resolution service from admin config
├── migrations/
│   ├── SignalStack.Migrations/       ← Mongo.Migration migrations (collection catalogue, indexes)
│   └── SignalStack.SqlMigrations/    ← FluentMigrator migrations for PostgreSQL historical schema
├── shared-types/    ← PLANNED: Cross-app DTOs, request/response contracts, domain enums
├── ui/              ← PLANNED: Shared React components, design tokens, layout primitives
└── testing/         ← PLANNED: Shared test helpers, fixtures, mocks, integration utilities
```

Do not put application business logic in packages. Packages are for types, utilities, and shared infrastructure that have no domain opinions.

---

## `infra/` — Infrastructure as code and local dev tooling

```
infra/
├── README.md
├── docker/
│   ├── docker-compose.yml            ← Local dev stack (MongoDB, PostgreSQL, Redis)
│   ├── .env.example                  ← Template for local secrets (copy to .env, never commit .env)
│   ├── .gitignore                    ← Excludes .env from source control
│   ├── up.ps1                        ← Convenience script: starts all containers
│   └── down.ps1                      ← Convenience script: stops and removes containers
└── azure/
    ├── worker-appservice.bicep        ← Worker App Service plan: pins workerCount=1, no auto-scale
    └── scripts/
        └── Test-WorkerSingletonPreflight.ps1  ← CI gate: fails if Worker plan has >1 instance
                                                   or an auto-scale rule attached
```

**Starting local dev:**
```powershell
cd infra/docker
cp .env.example .env   # fill in secrets
./up.ps1               # starts MongoDB, PostgreSQL, Redis
```

**Azure deployment note:** `worker-appservice.bicep` is load-bearing. It enforces the single-instance invariant at the infrastructure layer. Do not modify `workerCount` or add auto-scale rules without first completing the Phase C partitioning extension described in ADR-0003.

---

## `design_system/` — Brand tokens and UI kit

Reference material for building the portal UI. The design system defines the visual language; `apps/web` consumes it.

```
design_system/
├── README.md                    ← How to use the design system
├── SKILL.md                     ← Agent skill: how to apply design tokens when building UI
├── colors_and_type.css          ← CSS custom properties: all colour roles, typography scale,
│                                   spacing, shadows, and border radii
├── assets/
│   ├── logo-lockup.svg          ← Full logo (mark + wordmark, horizontal)
│   ├── logo-mark.svg            ← Icon-only mark
│   ├── logo-tile.svg            ← Square tile variant (app icons, favicons)
│   └── logo-wordmark.svg        ← Text-only wordmark
├── preview/                     ← Static HTML previews of every design token and component
│   ├── color-brand.html
│   ├── color-market.html        ← Up/down/neutral/circuit colour semantics
│   ├── color-status.html
│   ├── color-surfaces.html
│   ├── comp-badges.html
│   ├── comp-buttons.html
│   ├── comp-inputs.html
│   ├── comp-kpi.html
│   ├── comp-notifications.html
│   ├── comp-table.html
│   ├── spacing-*.html
│   └── type-*.html
└── ui_kits/
    ├── user-portal/             ← Reference React components for user-facing screens
    │   ├── AppShell.jsx         ← Navigation shell and layout primitives
    │   ├── Dashboard.jsx        ← Portfolio dashboard layout
    │   ├── ChartPage.jsx        ← Chart + RME advisory panel layout
    │   ├── OrderModal.jsx       ← Execution confirmation modal (Phase 1)
    │   ├── SignalBuilder.jsx    ← Signal Subscription builder UI
    │   ├── Primitives.jsx       ← Atomic design tokens as React components
    │   ├── index.html           ← Standalone HTML preview
    │   └── README.md
    └── admin-portal/            ← Reference React components for admin-facing screens
        ├── AdminShell.jsx       ← Admin navigation shell
        ├── AdminHome.jsx        ← Admin dashboard / operational status widget
        ├── AdminPages.jsx       ← Page-level layouts for all admin sections
        ├── index.html           ← Standalone HTML preview
        └── README.md
```

**When building UI in `apps/web/`:** import token values from `design_system/colors_and_type.css` (or mirror them into a Tailwind theme extension). Do not invent new colour values or typographic scales. Read `design_system/SKILL.md` before implementing new screens.

---

## `docs/` — Canonical documentation

All design decisions, product requirements, and architectural rules live here. This folder is the single source of truth. Code and skills are derived from it — not the other way around.

```
docs/
├── README.md                        ← Document map: role and editing rules for every file
├── terminology.md                   ← Canonical names for all components and domain concepts
│                                       (READ BEFORE naming anything)
├── requirements-spec.md             ← Source of truth for all product requirements (REQ-IDs)
├── product-scope.md                 ← Short product overview and major user journeys
├── system-architecture.md           ← How the system is shaped: components, flows, boundaries
├── implementation-roadmap.md        ← Phased delivery plan (P0 → P9); do not invent requirements here
├── engineering-standards.md         ← Coding, testing, security, and observability standards
├── system-config.md                 ← Runtime config tiers, sys_config schema, required seed table
├── portfolio-risk-guidelines.md     ← RME defaults: sizing models, stop types, drawdown rules
├── data-management.md               ← MongoDB collection catalogue: purpose, TTL, retention
├── repo-structure.md                ← Preferred monorepo layout rules (source for this file)
├── project-structure.md             ← THIS FILE: practical map of what exists and where to add code
├── review-prompt.md                 ← Prompt template used to request doc / code reviews
├── fyers_api_integration_guide.md   ← Reference copy of FYERS API v3 docs (REST + WebSocket)
├── adr/                             ← Architecture Decision Records
│   ├── 0001-documentation-structure.md
│   ├── 0002-observability-and-configuration.md
│   ├── 0003-rme-per-position-event-serialisation.md
│   ├── 0004-corporate-action-detection-threshold.md
│   └── 0005-mdp-server-side-websocket-migration-path.md
├── build_docs_reviews/              ← Accumulated review notes on documentation quality
│   ├── prompt.md                    ← Full review prompt used for doc-quality passes
│   └── review_*.md                  ← Review output reports
├── legal/                           ← Versioned user-facing legal content
│   └── README.md                    ← Index of tester acknowledgement, disclaimers, ToS, Privacy Policy
├── privacy/
│   └── ropa.md                      ← Internal Record of Processing Activities (REQ-PRIVACY-010)
└── operations/
    ├── fyers-api-budget.md          ← FYERS API call budget model (REQ-RATE-011)
    └── runbooks/
        └── README.md                ← Operational runbook catalogue (required before Phase B)
```

**Editing rules (from `docs/README.md`):**
- Add or change a product requirement in `requirements-spec.md` first.
- Update `system-architecture.md` only when a system boundary or component shape changes.
- Record significant design trade-offs in `docs/adr/` — one file per decision.
- Never duplicate requirements into skills or roadmap files; reference by REQ-ID instead.

---

## `execution_plan/` — AI agent task pipeline

The machine-readable task state for incremental, resumable, multi-agent execution. Every agent run reads this folder and writes back to it.

```
execution_plan/
├── agent.md            ← AGENT RUNBOOK: step-by-step procedure every agent must follow verbatim.
│                          Covers: load state → validate deps → evaluate active_when →
│                          execute one task → write task log → update status.json.
├── conventions.md      ← Strict operational rules: single task per run, deterministic state
│                          transitions, cross-agent interoperability contract.
├── execution_plan.md   ← The full ordered task list across all phases (P0–P9).
│                          Each task carries: Title, Depends on, REQ, Touches, Vertical slice,
│                          Verification, and optional Frozen-after-author / active_when.
├── milestones.md       ← Phase exit-readiness criteria and version checkpoints (v0.1 → v1.0).
├── status.json         ← AUTHORITATIVE STATE: current_task_id, completed_tasks, pending_tasks,
│                          deactivated_tasks, phase_coverage per phase, last_updated.
│                          ⚠ Only this file determines what has been done. Never infer
│                          progress from chat history or code inspection alone.
└── task_logs/          ← Append-only task execution records (one file per completed task)
    ├── .gitkeep
    ├── 20260421T000001Z__P0-T2.md
    ├── 20260421T001500Z__P1-T1.md
    ├── 20260421T180635Z__P1-T2.md
    ├── 20260425T103000Z__P0-T3.md
    ├── 20260425T111049Z__P1-T3.md
    ├── 20260425T112300Z__P1-T4.md
    └── 20260425T174924Z__P1-T5.md
```

**For AI agents — the resume protocol:**
1. Read `execution_plan/status.json`.
2. `current_task_id` is the next task to execute (if empty, take the first entry in `pending_tasks`).
3. Validate that all dependencies in `Depends on` are in `completed_tasks`. If not, execute the earliest missing dependency first.
4. Execute exactly one task. Do not batch.
5. Write the task log to `execution_plan/task_logs/YYYYMMDDTHHMMSSZ__{TASK_ID}.md`.
6. Update `status.json`: move the task to `completed_tasks`, set `current_task_id` to the next pending task.
7. See `execution_plan/agent.md` for the complete procedure.

---

## `.claude/skills/` — Focused build instructions for Claude agents

Short, task-specific execution aids consumed by Claude when working on a specific domain. These are derived from the canonical docs — they do not define requirements.

```
.claude/skills/
├── README.md                      ← How skills relate to canonical docs
├── backend-foundation.md          ← API + Worker bootstrap, config loading, observability
├── frontend-trading-ui.md         ← Next.js portal patterns, chart integration, design tokens
├── market-data-and-orders.md      ← MDP abstraction, FYERS adapter, execution widget
├── strategy-and-backtesting.md    ← Signal layer, EOD runner, backtest engine, RME
├── identity-and-integrations.md   ← OAuth, FYERS token flow, Telegram integration
├── admin-and-operations.md        ← Universe sync, job controls, sys_config management
├── risk-and-compliance.md         ← RME rules, position sizing, portfolio heat, guardrails
└── qa-and-release.md              ← Testing patterns, CI/CD, deployment verification
```

**When to read a skill file:** read the skill matching the domain of your current task immediately before writing code. Skills are concise and task-focused; for deeper context always go to the canonical `docs/` files they reference.

---

## Root-level agent configuration files

Three files at the repo root configure AI agent behaviour. They carry identical guardrails; which one an agent reads depends on the platform.

| File | Used by |
|------|---------|
| `CLAUDE.md` | Claude / Cowork agents |
| `AGENTS.md` | Codex / OpenAI agents |
| `RUNBOOK.md` | Any agent — single-line pointer to `execution_plan/agent.md` |

All three enforce the same non-negotiable guardrails. When in doubt, read `CLAUDE.md`; it is the most complete version.

---

## Where to put new code — decision guide

| What you are building | Where it goes |
|---|---|
| API endpoint or controller | `apps/api/Api/` |
| Domain entity or value object | `apps/api/Domain/` |
| Orchestration / application service | `apps/api/Services/` |
| MongoDB repository | `apps/api/Repositories/Mongo/` |
| PostgreSQL repository (OHLCV only) | `apps/api/Repositories/Sql/` |
| FYERS / Telegram / OAuth adapter | `apps/api/Integrations/{Provider}/` |
| Risk rule or sizing model | `apps/api/Risk/` |
| Admin workflow (universe, jobs) | `apps/api/Admin/` |
| CSV parsing / Nifty 500 sync | `apps/api/Admin/Imports/` |
| Config loading / sys_config reader | `apps/api/Config/` |
| Next.js page or layout | `apps/web/src/app/` |
| Feature module (bounded UI area) | `apps/web/src/features/{feature}/` |
| Shared UI component | `apps/web/src/components/` |
| API client / WebSocket helper | `apps/web/src/lib/` |
| Background job | `apps/worker/Jobs/{JobName}/` |
| Job scheduler / runner | `apps/worker/Runners/` |
| Worker utility / observability | `apps/worker/Lib/` |
| Worker bootstrap config | `apps/worker/Config/` |
| Cross-app DTO / contract | `packages/shared-types/` (when created) |
| Market Data Provider interface / DTO | `packages/market-data/SignalStack.MarketData/` |
| Shared React primitive | `packages/ui/` (when created) |
| Azure IaC | `infra/azure/` |
| Local dev container change | `infra/docker/` |
| Product requirement change | `docs/requirements-spec.md` |
| Architecture decision | `docs/adr/` |
| New sys_config key | `docs/system-config.md` (seed table) AND code |
| Test for API code | `tests/api/SignalStack.Api.Tests/` |
| Test for Worker code | `tests/worker/SignalStack.Worker.Tests/` |

---

## Key invariants every contributor must respect

These are enforced by `CLAUDE.md` / `AGENTS.md` and checked by CI:

- **Long-only, NSE Nifty 500 only.** No short-side workflows. No instruments outside the universe.
- **No autonomous order placement.** Every order action is user-initiated. The backend never calls the FYERS order-placement REST API directly.
- **MDP abstraction boundary.** All market-data ingestion goes through the `IMarketDataProvider` interface. No direct FYERS calls outside the adapter.
- **No user tokens for shared ingestion.** The shared MDP (used by LMDS, DS, EODSR) uses the admin FYERS token only.
- **PostgreSQL = historical OHLCV only.** User-specific operational state lives in MongoDB.
- **Worker single-instance invariant.** Exactly one Worker instance. No auto-scale until ADR-0003 Phase C is complete.
- **Startup config independence.** Apps must be able to start logging and load bootstrap config before MongoDB is reachable.
- **No secrets in source control.** Use environment variables and Azure Key Vault. Never commit `.env` files.
