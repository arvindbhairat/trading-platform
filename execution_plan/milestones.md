# Milestones (Versioned Delivery Checkpoints)

Versions are **delivery checkpoints**. A version is “shippable” when its acceptance criteria are met and the included tasks are completed in `execution_plan/status.json`.

## v0.1 — Versioned Execution Plan System

- **Included tasks**: P0-T1
- **Acceptance criteria**:
  - `execution_plan/` exists with:
    - `execution_plan.md`
    - `milestones.md`
    - `status.json`
    - `conventions.md`
    - `task_logs/` directory
  - `status.json` lists every task as either completed or pending (no missing tasks)
  - An agent can deterministically identify the next task using `status.json`

## v0.2 — Platform Foundation Slice

- **Included tasks**: P1-T1 .. P1-T6
- **Acceptance criteria**:
  - `apps/web`, `apps/api`, and `apps/worker` have runnable local baselines
  - Local dev stack dependencies (MongoDB, SQL Server, Redis) can be started from `infra/`
  - API and Worker emit OTLP telemetry to a local collector
  - Worker singleton invariant scaffolding is present and treated as frozen scaffolding

## v0.3 — Identity, Access, and Configuration Baseline

- **Included tasks**: P2-T1 .. P2-T9
- **Acceptance criteria**:
  - OAuth sign-in works end-to-end and protected endpoints enforce authorization
  - Admin bootstrap via `SEED_ADMIN_EMAIL` is implemented and tested
  - `sys_config` seeding and migration ordering are enforced by pipeline and runtime sentinel checks
  - Admin UI supports `sys_config` management and user approval flows
  - FYERS CNC sandbox verification outcome is recorded and the plan branch decision is captured

## v0.4 — Universe and Market Data (Phase A)

- **Included tasks**: P3-T1 .. P3-T9
- **Acceptance criteria**:
  - Nifty 500 universe ingestion (admin CSV) and exclusions/archives are enforced
  - Trading calendar is canonical and consistent with IST scheduling rules
  - MDP abstraction exists and domain code is provider-agnostic
  - Historical candles are persisted in SQL Server and served via API for charting
  - HDS is operator-triggered, resumable, and idempotent; DS is scheduled and recovery-buffered

## v0.5 — Strategy Research (Backtesting)

- **Included tasks**: P4-T1 .. P4-T4
- **Acceptance criteria**:
  - Strategy definitions are versioned with parameter sets
  - Backtests are deterministic and results are persisted and viewable
  - Shared timeframe aggregation is consistent across charting and backtesting inputs

## v0.6 — Live Operations (Signals + Notifications)

- **Included tasks**: P5-T1 .. P5-T5
- **Acceptance criteria**:
  - EODSR runs only after DS success markers
  - Notification records are the source of truth; Telegram is a delivery channel with retry and status tracking
  - Portal notification feed supports filtering, unread indicators, and deep links

## v0.7 — Portfolio Analytics (Phase A subset)

- **Included tasks**: P6-T1 .. P6-T4
- **Acceptance criteria**:
  - Account sync supports backfill and incremental updates
  - Trade ledger ingestion is append-only and idempotent
  - Reconciliation/mismatch detection meets REQ-RECON-001..004 (Phase A scope)
  - Holdings + PnL views are usable for approved users

## v0.8 — Risk Management Engine (RME) Integration

- **Included tasks**: P7-T1 .. P7-T6
- **Acceptance criteria**:
  - Calibration note prerequisite is committed before build
  - RME lifecycle state machine and plugins are implemented with high test coverage gates
  - RME is wired into both backtesting and live workflows using a single implementation
  - Portal surfaces RME advisory and portfolio health consistently

## v0.9 — Execution Assistance (Conditional)

- **Included tasks**: P8-T1 .. P8-T6 (only if Phase 1 CNC verification passed)
- **Acceptance criteria**:
  - SDK pinning + contract test gating is in place
  - Signed payload + nonce replay protections are enforced server-side
  - Intent ledger is written before widget activation; callback only mutates intent state
  - LADS reconciliation drives lifecycle transitions and user notifications; UI shows awaiting-confirmation states

## v1.0 — V1 Hardening + Operations + Corporate Actions

- **Included tasks**: P9-T1 .. P9-T4
- **Acceptance criteria**:
  - Operators have monitoring and safe manual retry controls for critical jobs
  - Required runbooks and alerting/killswitches exist and are validated
  - Corporate action handling maintains continuity across charts/backtests/holdings
  - Regression coverage and failure-mode hardening gates are met for critical workflows

