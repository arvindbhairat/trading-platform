# Versioned Execution Plan

This folder defines a **deterministic, resumable execution model** for building the platform in small, independently executable tasks. It is designed for **cross-agent interoperability**: any agent can resume from `status.json` without needing prior conversational context.

## How to use (execution model)

- **First run (bootstrap)**: Create `execution_plan/` artifacts and seed `status.json`. (This repo run is completed as `P0-T1`.)
- **Every subsequent run**:
  - Read `execution_plan/status.json`
  - Identify the **next** task: `current_task_id` (or first item in `pending_tasks`)
  - Execute **only that task**
  - Update `status.json` + any relevant docs + add a run log under `task_logs/`

Strict operating rules are defined in `conventions.md`.

## Task definition contract (must be true for every task)

Each task in this plan is:

- **Atomic**: can be completed in a single run by one agent.
- **Sequential**: the plan order is the default execution order; dependencies define the minimum constraints.
- **Independently executable**: clear entry conditions, concrete outputs, and verification criteria.
- **Idempotent-by-design**: re-running after partial failure should converge without duplicating records or corrupting state.
- **Version-mapped**: every task targets a version checkpoint in `milestones.md`.

## Dependency semantics

- A task may list dependencies (task IDs).
- **An agent must not start a task unless all dependencies are in `completed_tasks`.**
- If a dependency is missing, the agent must execute the earliest pending dependency first (by plan order).

## Phases and tasks

### Phase P0 — Execution Plan System (v0.1)

#### P0-T1 (v0.1)
- **Title**: Bootstrap versioned execution plan system
- **Depends on**: none
- **Steps / outputs**:
  - Create `execution_plan/` folder structure
  - Write `execution_plan.md`, `milestones.md`, `conventions.md`
  - Seed `status.json` with deterministic ordering
- **Done when**:
  - All required files exist and `status.json` lists all remaining tasks in `pending_tasks`

#### P0-T2 (v0.1)
- **Title**: Add single-line continuation runbook for agents
- **Depends on**: P0-T1
- **Steps / outputs**:
  - Add `execution_plan/agent.md` with deterministic “resume-next-task” procedure
  - Add root `RUNBOOK.md` as a stable pointer to `execution_plan/agent.md`
  - Ensure conventions align with “execute exactly one task per run” and “update status + task log”
- **Done when**:
  - An agent can be instructed with a single line (“Continue using `execution_plan/agent.md`” or “Continue using `RUNBOOK.md`”) and deterministically resumes from `status.json`

---

### Phase P1 — Platform Phase 0: Foundation (v0.2)

#### P1-T1 (v0.2)
- **Title**: Establish baseline solution/workspace scaffolding
- **Depends on**: P0-T1
- **Expected outcomes**:
  - Repo structure matches `docs/repo-structure.md` intent (`apps/web`, `apps/api`, `apps/worker`, `packages/*`, `infra/*`)
  - Local build/test entrypoints exist for each app (even if minimal)

#### P1-T2 (v0.2)
- **Title**: Local dev environment: containers and bootstrap docs
- **Depends on**: P1-T1
- **Expected outcomes**:
  - Local-first runnable stack for MongoDB, SQL Server, Redis (compose/scripts under `infra/`)
  - Minimal run instructions captured in `infra/README.md` (or equivalent canonical place)

#### P1-T3 (v0.2)
- **Title**: Backend API baseline (.NET) with health checks + versioned API root
- **Depends on**: P1-T1
- **Expected outcomes**:
  - `apps/api` runs locally
  - Health endpoint(s) and a versioned API route prefix exist
  - Structured logging baseline is present (per `docs/engineering-standards.md`)

#### P1-T4 (v0.2)
- **Title**: Worker baseline (.NET) with singleton invariants scaffold
- **Depends on**: P1-T1
- **Expected outcomes**:
  - `apps/worker` runs locally
  - Single-instance invariant scaffolding is present per roadmap Phase 0 (IaC + pipeline gate + Redis lease pattern) **as authored once**

#### P1-T5 (v0.2)
- **Title**: Observability baseline: OpenTelemetry + OTLP wiring
- **Depends on**: P1-T3, P1-T4
- **Expected outcomes**:
  - Logs/traces/metrics can be emitted from API and Worker over OTLP to a local collector

#### P1-T6 (v0.2)
- **Title**: Configuration baseline: Azure App Configuration + Key Vault bootstrap pattern
- **Depends on**: P1-T3, P1-T4
- **Expected outcomes**:
  - Bootstrap configuration pattern exists and is shared
  - No secrets committed; local dev uses safe placeholders

---

### Phase P2 — Platform Phase 1: Identity and Access (v0.3)

#### P2-T1 (v0.3)
- **Title**: OAuth sign-in end-to-end (portal ↔ API)
- **Depends on**: P1-T3
- **Expected outcomes**:
  - User can sign in via OAuth provider(s) and obtain Bearer token for API calls
  - Auth boundary is enforced on a protected endpoint

#### P2-T2 (v0.3)
- **Title**: Core identity model + roles + approval state
- **Depends on**: P2-T1
- **Expected outcomes**:
  - MongoDB models exist for user/account/role/approval per requirements
  - Role checks available to API endpoints

#### P2-T3 (v0.3)
- **Title**: Lazy bootstrap admin per `SEED_ADMIN_EMAIL`
- **Depends on**: P2-T2
- **Expected outcomes**:
  - First OAuth sign-in with `SEED_ADMIN_EMAIL` creates admin record and assigns admin role in the same transaction

#### P2-T4 (v0.3)
- **Title**: FYERS credential management + token lifecycle handling
- **Depends on**: P2-T2
- **Expected outcomes**:
  - Secure credential storage pattern implemented (Key Vault/env), with server-side redaction guarantees
  - Dirty-token / re-auth-required UX wiring available in portal

#### P2-T5 (v0.3)
- **Title**: `sys_config` seeder console app + manifest (insert-missing-only)
- **Depends on**: P1-T3
- **Expected outcomes**:
  - Dedicated seeder job exists; reads manifest; inserts missing only; never overwrites
  - Emits a structured report artifact

#### P2-T6 (v0.3)
- **Title**: Deployment preflight order gates for migrations + seeding
- **Depends on**: P2-T5
- **Expected outcomes**:
  - Pipeline order matches: SQL migrations → Mongo migrations → `sys_config` seeder → activate API/Worker
  - API/Worker startup fails fast if `platform.seed.version` sentinel is absent

#### P2-T7 (v0.3)
- **Title**: Admin portal: manage `sys_config` runtime settings
- **Depends on**: P2-T2, P2-T6
- **Expected outcomes**:
  - Admin UI can view and edit mutable runtime settings with auditing

#### P2-T8 (v0.3)
- **Title**: Admin approval flows
- **Depends on**: P2-T2
- **Expected outcomes**:
  - Admin can approve/reject users; non-approved users are blocked from protected platform features

#### P2-T9 (v0.3)
- **Title**: FYERS CNC sandbox verification gate (record outcome)
- **Depends on**: P2-T4
- **Expected outcomes**:
  - Minimal `<fyers-button>` page verified for CNC in sandbox
  - Result recorded as an ADR note; plan branching decision captured

---

### Phase P3 — Platform Phase 2: Universe and Market Data (v0.4)

#### P3-T1 (v0.4)
- **Title**: Confirm FYERS bulk quote rate-limit accounting + record ops note
- **Depends on**: P2-T4
- **Expected outcomes**:
  - `docs/operations/fyers-api-budget.md` updated with verified accounting outcome and any adjusted default `jobs.live_market_scan.poll_interval_seconds`

#### P3-T2 (v0.4)
- **Title**: Symbol master + universe state models
- **Depends on**: P1-T3
- **Expected outcomes**:
  - MongoDB models for symbol master and universe state are defined; deterministic symbol→SQL table mapping strategy established

#### P3-T3 (v0.4)
- **Title**: Admin CSV universe upload with archive/exclusion handling
- **Depends on**: P3-T2, P2-T7
- **Expected outcomes**:
  - Admin can upload and manage Nifty 500 universe CSV; archive/exclusion rules persist and are enforced

#### P3-T4 (v0.4)
- **Title**: Internal trading calendar + admin management screens
- **Depends on**: P2-T7
- **Expected outcomes**:
  - Canonical IST trading calendar logic exists and is admin-manageable

#### P3-T5 (v0.4)
- **Title**: Market Data Provider (MDP) interface + FYERS adapter
- **Depends on**: P3-T2
- **Expected outcomes**:
  - Common MDP interface exists; FYERS implementation is the only concrete adapter in Phase A; domain code depends only on interface

#### P3-T6 (v0.4)
- **Title**: Historical schema + chart data endpoints (SQL Server-backed)
- **Depends on**: P3-T5
- **Expected outcomes**:
  - SQL Server schema for OHLCV is defined
  - API endpoints serve historical candles for charting

#### P3-T7 (v0.4)
- **Title**: HistoricDataSeed (HDS) operator job: batched + resumable
- **Depends on**: P3-T6
- **Expected outcomes**:
  - HDS can seed history with per-symbol idempotent table materialization and resumable progress markers

#### P3-T8 (v0.4)
- **Title**: DataSync (DS) job: daily recovery-buffer sync
- **Depends on**: P3-T7
- **Expected outcomes**:
  - DS runs daily and backfills last N sessions as recovery buffer; emits success markers

#### P3-T9 (v0.4)
- **Title**: Live quote flow routed through MDP + portal view
- **Depends on**: P3-T5
- **Expected outcomes**:
  - Portal can display live quotes; provider selection is admin-configured

---

### Phase P4 — Platform Phase 3: Strategy Research (v0.5)

#### P4-T1 (v0.5)
- **Title**: Signal subscription model: definitions, versions, parameter sets
- **Depends on**: P2-T2
- **Expected outcomes**:
  - Strategy definitions can be stored with versioned parameter sets

#### P4-T2 (v0.5)
- **Title**: Historical query layer + shared timeframe aggregation
- **Depends on**: P3-T6
- **Expected outcomes**:
  - Shared timeframe logic used consistently for charting and backtesting inputs

#### P4-T3 (v0.5)
- **Title**: Backtest engine + result storage
- **Depends on**: P4-T2
- **Expected outcomes**:
  - Deterministic backtest run and persisted results for later viewing

#### P4-T4 (v0.5)
- **Title**: Portal: Signal Builder + Backtest Results screens
- **Depends on**: P4-T3
- **Expected outcomes**:
  - User can build a strategy configuration and run/view backtests

---

### Phase P5 — Platform Phase 4: Live Operations (v0.6)

#### P5-T1 (v0.6)
- **Title**: EOD success marker contract + DS/EODSR sequencing
- **Depends on**: P3-T8
- **Expected outcomes**:
  - EODSR runs only after verifiable DS success marker

#### P5-T2 (v0.6)
- **Title**: EOD Signal Runner (EODSR)
- **Depends on**: P5-T1, P4-T1
- **Expected outcomes**:
  - EOD signal generation produces durable notification records (no direct Telegram-as-source-of-truth)

#### P5-T3 (v0.6)
- **Title**: Notifications store + Telegram delivery worker with retry/backoff
- **Depends on**: P5-T2
- **Expected outcomes**:
  - MongoDB notification records are authoritative; Telegram delivery status is tracked with retries

#### P5-T4 (v0.6)
- **Title**: Telegram bot provisioning + user deep-link subscription
- **Depends on**: P5-T3
- **Expected outcomes**:
  - Users can subscribe via deep link; deliveries route correctly

#### P5-T5 (v0.6)
- **Title**: Portal notification feed (filtering, unread, deep links)
- **Depends on**: P5-T3
- **Expected outcomes**:
  - In-portal feed shows notifications with reliable unread state and deep links

---

### Phase P6 — Platform Phase 5: Portfolio Analytics (v0.7)

#### P6-T1 (v0.7)
- **Title**: Trades/holdings/positions/orders sync (FYERS via user token)
- **Depends on**: P2-T4
- **Expected outcomes**:
  - Sync pipeline supports first-time backfill and incremental refresh

#### P6-T2 (v0.7)
- **Title**: Immutable trade ledger + idempotent ingestion
- **Depends on**: P6-T1
- **Expected outcomes**:
  - Append-only trade ledger is authoritative; replays are deduplicated

#### P6-T3 (v0.7)
- **Title**: Reconciliation + mismatch detection (Phase A subset)
- **Depends on**: P6-T2
- **Expected outcomes**:
  - Reconciliation and mismatch detection implemented per REQ-RECON-001..004 (UI-heavy REQ-RECON-005..008 deferred)

#### P6-T4 (v0.7)
- **Title**: Holdings + PnL + reconciliation views
- **Depends on**: P6-T3
- **Expected outcomes**:
  - Portal renders holdings and analytics views; mismatches surfaced per Phase A scope

---

### Phase P7 — Platform Phase 6: Risk Management Engine (v0.8)

#### P7-T1 (v0.8)
- **Title**: Commit corporate-action calibration note (ADR-0004 requirement)
- **Depends on**: P6-T1
- **Expected outcomes**:
  - Calibration note exists and is committed before RME build work proceeds

#### P7-T2 (v0.8)
- **Title**: RME core module + plugin interfaces + state machine
- **Depends on**: P7-T1
- **Expected outcomes**:
  - Standalone RME module exists with explicit lifecycle states; strategy-independent interfaces are defined

#### P7-T3 (v0.8)
- **Title**: Position sizing + stop mechanisms (pluggable)
- **Depends on**: P7-T2
- **Expected outcomes**:
  - Required sizing and stop plugins implemented and configurable via profiles

#### P7-T4 (v0.8)
- **Title**: Portfolio heat + drawdown tracking + advisory computations
- **Depends on**: P7-T3
- **Expected outcomes**:
  - Heat and drawdown rules enforced; advisory outputs computed deterministically

#### P7-T5 (v0.8)
- **Title**: Wire RME into backtests + live workflows (shared implementation)
- **Depends on**: P7-T4, P4-T3, P5-T2
- **Expected outcomes**:
  - Same RME logic drives both backtests and live signal flows

#### P7-T6 (v0.8)
- **Title**: Portal: RME advisory panels + dashboard summary
- **Depends on**: P7-T5
- **Expected outcomes**:
  - Chart page + dashboard surface RME advisory and portfolio health

---

### Phase P8 — Platform Phase 7: Execution Assistance (v0.9) (conditional)

#### P8-T1 (v0.9)
- **Title**: Embed FYERS API Connect SDK + hash/contract-test gate
- **Depends on**: P2-T9
- **Expected outcomes**:
  - SDK is pinned via config; detected changes trigger contract suite

#### P8-T2 (v0.9)
- **Title**: Pre-flight check endpoint (token validity, kill switch, drawdown block, dedupe)
- **Depends on**: P8-T1, P7-T4
- **Expected outcomes**:
  - Server validates eligibility for execution assistance before widget invocation

#### P8-T3 (v0.9)
- **Title**: Signed order payload endpoint (HMAC + nonce) + validation
- **Depends on**: P8-T2
- **Expected outcomes**:
  - Payload signing/validation prevents tampering and nonce replay

#### P8-T4 (v0.9)
- **Title**: Intent ledger collection + intent write before widget activation
- **Depends on**: P8-T3
- **Expected outcomes**:
  - `intent_ledger` exists with required indexes; no activation path bypasses intent write

#### P8-T5 (v0.9)
- **Title**: Finished callback handler writes intent status only; LADS reconciliation net
- **Depends on**: P8-T4
- **Expected outcomes**:
  - Callback never advances position lifecycle; LADS confirms fills; orphan/unresolved intents are handled

#### P8-T6 (v0.9)
- **Title**: Portal awaiting-confirmation states + LADS-gated user notifications
- **Depends on**: P8-T5
- **Expected outcomes**:
  - UI reflects awaiting confirmation; Telegram/feed events trigger only on LADS-confirmed fills

---

### Phase P9 — Platform Phase 8/9: Admin Operations + Hardening + Corporate Actions (v1.0)

#### P9-T1 (v1.0)
- **Title**: Admin job monitoring + manual retry controls
- **Depends on**: P5-T3, P3-T8
- **Expected outcomes**:
  - Operators can observe job runs, failures, and trigger safe retries

#### P9-T2 (v1.0)
- **Title**: Operational safeguards + runbooks + alerting hardening
- **Depends on**: P9-T1
- **Expected outcomes**:
  - Required Phase A runbooks exist; key alerts/killswitches are present and tested

#### P9-T3 (v1.0)
- **Title**: Corporate actions + symbol continuity handling
- **Depends on**: P3-T6, P6-T2
- **Expected outcomes**:
  - Splits/bonuses/dividends continuity rules applied across charts/backtests/holdings

#### P9-T4 (v1.0)
- **Title**: V1 hardening pass: failure-injection fixes + regression coverage gates
- **Depends on**: P9-T2, P9-T3
- **Expected outcomes**:
  - Critical workflows have end-to-end tests; coverage and contract-test gates met per `docs/engineering-standards.md`

