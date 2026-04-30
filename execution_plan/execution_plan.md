# Versioned Execution Plan

This file is the deterministic, resumable execution plan for building the platform in small, independently executable tasks. It is designed for **cross-agent interoperability**: any agent can resume from `status.json` without prior conversational context.

Plan revision applied 2026-04-25 per `docs/build_docs_reviews/execution_plan_review_2026_04_25.md`. The previous task list is preserved through `completed_tasks` history in `status.json`.

## How to use (execution model)

- Read `execution_plan/status.json` to see what is done and what is next.
- Identify the next task (`current_task_id`, or first item in `pending_tasks`).
- Execute **only that task** per `agent.md`.
- Update `status.json`, write a task log, and run phase-gate verification when applicable.

Strict operating rules are in `conventions.md`. The agent runbook is in `agent.md`.

## Task definition contract

Each task carries:

- **Title** — imperative summary.
- **Depends on** — task IDs that must be in `completed_tasks` first.
- **REQ** — REQ-IDs (and ADR / engineering-standards section anchors) the task satisfies. Used by phase-gate verification.
- **Touches** — components changed.
- **Vertical slice** — the smallest end-to-end demonstration of the change.
- **Verification** — how to prove the task is done; maps back to each REQ-ID.
- **Frozen-after-author** *(optional)* — once authored, must not be re-opened in any subsequent phase without an ADR amendment.
- **active_when** *(optional)* — predicate that must hold for the task to be executed.

## Dependency semantics

- A task may not start unless all dependencies are in `completed_tasks`.
- If a dependency is missing, the agent must execute the earliest pending dependency first (by plan order).
- If `active_when` is false, the task is moved to `deactivated_tasks` and skipped.

---

## Phase P0 — Execution Plan System (v0.1)

### P0-T1 — Bootstrap versioned execution plan system
- **Status:** completed.
- **Depends on:** none.
- **REQ:** none direct; substrate for all subsequent tasks.
- **Vertical slice:** files exist; agent can resume from `status.json`.

### P0-T2 — Add single-line continuation runbook
- **Status:** completed.
- **Depends on:** P0-T1.
- **REQ:** none direct.
- **Vertical slice:** `RUNBOOK.md` and `agent.md` enable single-line resume.

### P0-T3 — Strengthen `agent.md` and `conventions.md` for REQ-ID coverage, conflict resolution, phase-gate verification, Frozen-after-author
- **Status:** completed (applied 2026-04-25 with the revised plan adoption).
- **Depends on:** P0-T2.
- **REQ:** none direct; supports REQ-NFR-014 traceability and the Phase 0 "One-time scaffolding" framing in `docs/implementation-roadmap.md`.
- **Touches:** `execution_plan/agent.md`, `execution_plan/conventions.md`, `execution_plan/status.json` schema (added `phase_coverage`, `deactivated_tasks`), `execution_plan/execution_plan.md`, `execution_plan/milestones.md`.
- **Vertical slice:** every agent run reports REQ coverage delta; `agent.md` step 6 enforces conflict-resolution branching; phase-gate verification is mandated; Frozen-after-author tasks cannot be silently re-opened.
- **Verification:** files committed; `status.json` carries the new fields; this task's log records the change.

---

## Phase P1 — Foundation, SRE Scaffolding, Data Foundation (v0.2)
*All tasks below are **Frozen-after-author** per the "One-time scaffolding (author once, freeze until phase gate)" paragraph in `docs/implementation-roadmap.md` Phase 0. Do not re-open in P2..P7 without an ADR amendment.*

### P1-T1 — Solution / workspace scaffolding
- **Status:** completed.
- **Depends on:** P0-T1.
- **REQ:** REQ-TECH-001/002, repo-structure.md.
- **Touches:** `apps/web`, `apps/api`, `apps/worker`, `packages/*`, `.github/workflows/ci.yml`.
- **Frozen-after-author.**

### P1-T2 — Local dev containers + bootstrap docs
- **Status:** completed.
- **Depends on:** P1-T1.
- **REQ:** infrastructure substrate.
- **Touches:** `infra/docker/*`, `infra/README.md`.
- **Frozen-after-author.**

### P1-T3 — API baseline (.NET) + versioned API root + structured logging
- **Depends on:** P1-T1.
- **REQ:** REQ-TECH-002, REQ-TECH-003, REQ-NFR-015.
- **Touches:** `apps/api`, `infra/`.
- **Vertical slice:** `GET /api/v1/healthz` returns 200; one structured log line written per request through `ILogger`.
- **Verification:** integration test asserts the route is prefixed `/api/v1` and the log payload carries the required enrichers (service, environment, trace).
- **Frozen-after-author.**

### P1-T4 — Worker baseline (.NET) + three-layer Worker singleton enforcement
- **Depends on:** P1-T1.
- **REQ:** REQ-RME-CONC-006.
- **Touches:** `apps/worker`, `infra/azure` (App Service plan IaC), `.github/workflows/`.
- **Vertical slice:** worker boots and runs a heartbeat loop; with two instances configured, the second instance crash-loops with a `singleton_violation` log; pipeline preflight check fails the build if the App Service plan has any auto-scale rule.
- **Verification:** unit test simulates the singleton lease conflict; an IaC lint test confirms `workerCount = 1` and no auto-scale rule.
- **Frozen-after-author.**

### P1-T5 — OpenTelemetry + OTLP wiring + collector dual-replica + saturation policy + fallback endpoint
- **Depends on:** P1-T3, P1-T4.
- **REQ:** REQ-NFR-008, REQ-NFR-009, REQ-NFR-009a, ADR-0002.
- **Touches:** `apps/api`, `apps/worker`, `infra/`.
- **Vertical slice:** API and Worker emit logs / traces / metrics over OTLP to a local collector; with the primary collector unreachable, telemetry is re-routed to the fallback endpoint by configuration only; non-blocking telemetry export verified.
- **Frozen-after-author.**

### P1-T6 — Azure App Configuration + Key Vault bootstrap + LKG cache + staleness alert
- **Depends on:** P1-T3, P1-T4.
- **REQ:** REQ-CONFIG-001/004/006/008, REQ-NFR-010/011/012, ADR-0002.
- **Touches:** `apps/api`, `apps/worker`, `packages/config`.
- **Vertical slice:** apps boot from env + App Config + Key Vault; with App Config unreachable, LKG cache serves last-known values up to `config.last_known_good.max_age_seconds`; staleness alert fires past `config.last_known_good.staleness_alert_threshold_seconds`.
- **Frozen-after-author.**

### P1-T7 — Serilog destructuring redaction policy + unit test
- **Depends on:** P1-T3, P1-T4, P1-T5.
- **REQ:** `docs/engineering-standards.md` § Security Standards (token / secret redaction in structured logs).
- **Touches:** `apps/api`, `apps/worker`, `packages/config` (logger setup).
- **Vertical slice:** unit test asserts a `LogEvent` containing a property named `access_token` (or any alias in the pattern list) is written with value `[REDACTED]` to the in-memory sink, including across OTLP export.
- **Frozen-after-author.**

### P1-T8 — Security baseline: TLS, security headers, CSP nonce, HSTS, X-Frame-Options, Permissions-Policy, WAF
- **Depends on:** P1-T3.
- **REQ:** REQ-SEC-001/002/003/004/005/006.
- **Touches:** `apps/web`, `apps/api`, `infra/azure` (Azure Front Door + WAF).
- **Vertical slice:** `curl -I` of any portal page shows the full required header set; CSP nonce-only inline scripts; auth endpoint returns HTTP 429 over the rate-limit threshold.
- **Frozen-after-author.**

### P1-T9 — CI gates: dep scan, SAST, secret scan, axe-core, contract-diff scaffolding, coverage thresholds
- **Depends on:** P1-T3, P1-T4.
- **REQ:** REQ-SEC-007/008/009, REQ-NFR-014/016, REQ-ACCESS-001.
- **Touches:** `.github/workflows/`, `tests/`, `packages/testing`.
- **Vertical slice:** sample PR with a high-severity CVE fails build; sample committed secret fails build; sample portal page with a WCAG fail breaks axe scan; contract-diff scaffolding runs (no contracts yet to compare); coverage thresholds enforced (70% .NET, 60% TS).
- **Frozen-after-author** for the gate list; the tests behind the gates evolve with code.

### P1-T10 — BCP / backup configuration + Key Vault hardening
- **Depends on:** P1-T6.
- **REQ:** REQ-BCP-001..008, REQ-LEGAL-009.
- **Touches:** `infra/azure`, `docs/operations/runbooks/`.
- **Vertical slice:** Mongo Atlas PITR enabled with >=24h retention; SQL Server market-data and backtest backups configured per RPO/RTO; Key Vault soft-delete + 90d + purge protection on; geo-redundant secondary in South India.
- **Frozen-after-author.**

### P1-T11 — Provision the full `data-management.md` collection catalogue
- **Depends on:** P1-T6.
- **REQ:** REQ-DATA-001..006a; every collection in `docs/data-management.md`.
- **Touches:** `apps/api`, `apps/worker`, Mongo.Migration migration set.
- **Vertical slice:** running migrations against an empty Mongo creates every catalogued collection (`users`, `sessions`, `fyers_tokens`, `symbol_master`, `symbol_health`, `trading_calendar`, `sys_config`, `watchlists`, `signals`, `signal_runs`, `entry_signals`, `notifications`, `notification_bots`, `positions`, `rme_events`, `rme_incidents`, `trade_ledger`, `intent_ledger`, `broker_orders`, `equity_curve`, `portfolio_snapshots`, `manual_adjustments`, `fyers_account_sync`, `rme_profile_recommendations`, `universe_upload_results`, `audit_events`, `job_runs`) with prescribed indexes, TTL, and Online Archive policies.
- **Frozen-after-author** for the schema; future evolution flows through REQ-MIGRATION-001/004.

### P1-T12 — ADR-0003 per-position channel registry skeleton
- **Depends on:** P1-T4.
- **REQ:** REQ-RME-CONC-001/002/003/004/005.
- **Touches:** `apps/worker` (`PositionChannelRegistry`, per-position consumer task, idle-channel cap).
- **Vertical slice:** unit test queues two events for the same position from two threads and observes serialised processing; backlog gauge `rme.channel.backlog_warn_depth` emits.
- **Frozen-after-author.**

### P1-T13 — Per-user trade-ledger write lock primitives
- **Depends on:** P1-T6.
- **REQ:** REQ-PORT-031 / REQ-PORT-031a / REQ-PORT-031b primitives only (writers wired in P5).
- **Touches:** `apps/api`, `apps/worker`, `packages/config`.
- **Vertical slice:** Redis lock acquired with fencing token; lease renewed at `jobs.ledger_lock.renewal_interval_seconds`; `ledger_snapshot_version` helper increments only after durable commit; fencing-token abort path does not advance the version (REQ-PORT-031b clarification).
- **Frozen-after-author.**

### P1-T14 — Phase-gate verification for Phase 1
- **Depends on:** P1-T1..T13, P1-T15.
- **Vertical slice:** every REQ-ID scoped to Phase 1 appears in `status.json.phase_coverage["P1"]`; no Phase 1 task has been re-opened by a later run; `milestones.md` § v0.2 acceptance criteria hold.

### P1-T15 — Conflict resolution: correct milestones.md v0.2 REQ-BCP scope
- **Depends on:** P1-T1..T13.
- **REQ:** none direct (plan-doc correction only).
- **Touches:** `execution_plan/milestones.md`.
- **Vertical slice:** `milestones.md` v0.2 acceptance criterion for BCP says "REQ-BCP-001..008" (not ..009); REQ-BCP-009 appears under v0.3 (where P2-T4 is assigned it); no other phase's acceptance criteria are changed.
- **Verification:** grep confirms "REQ-BCP-009" does not appear in the v0.2 section of milestones.md; it appears in the v0.3 section.
- **Conflict resolved:** REQ-BCP-009 (admin MFA enforcement) is an identity-layer requirement implemented in P2-T4. The v0.2 milestone text incorrectly grouped it with backup-configuration requirements (BCP-001..008). This task corrects the milestones.md authoring error so P1-T14 phase-gate can complete.

---

## Phase P2 — Identity, Sessions, Legal/Privacy, Admin Bootstrap, sys_config (v0.3)

### P2-T1 — OAuth sign-in (three providers) + Bearer JWT auth boundary + CSRF
- **Depends on:** P1-T3, P1-T8.
- **REQ:** REQ-AUTH-001/002/011, REQ-SEC-001, engineering-standards § CSRF model.
- **Vertical slice:** sign-in via Google, Microsoft, and Facebook all succeed; protected endpoint requires Bearer token in `Authorization` header; CSRF token enforced on every POST/PUT/PATCH/DELETE.

### P2-T2 — `sessions` MongoDB collection + lifecycle screens
- **Depends on:** P2-T1.
- **REQ:** REQ-SESSION-001/002/002a/007/008/010/011/012/013.
- **Vertical slice:** session doc TTL-expires at 24h; new login invalidates prior session; expiry-warning indicator at `ui.session.expiry_warning_minutes`; pending-approval and FYERS-required screens render.

### P2-T3 — Identity model + roles + approval state + Phase A tester ceiling
- **Depends on:** P2-T2.
- **REQ:** REQ-ROLE-001..004, REQ-LEGAL-003.
- **Vertical slice:** approval flow refuses when count reaches `operations.phase_a.tester_ceiling`; counter decrements on deactivation.

### P2-T4 — Lazy admin bootstrap + admin OAuth restricted to Google/Microsoft + `amr` MFA enforcement
- **Depends on:** P2-T3.
- **REQ:** REQ-ROLE-005/007/007a, REQ-BCP-009.
- **Vertical slice:** first sign-in by `SEED_ADMIN_EMAIL` creates the admin record + assigns the role atomically; admin sign-in without an MFA `amr` claim is refused; Facebook attempt for the admin email is blocked with the prescribed message.
- **Conflict found:** REQ-ROLE-007a (transfer recovery summary) is a frontend admin-dashboard feature requiring `job_runs` data, not a backend-auth scope. P2-T4-CR created to resolve.

### P2-T4-CR — Conflict resolution: remove REQ-ROLE-007a from P2-T4 scope
- **Depends on:** none (plan-document correction).
- **REQ:** none direct (plan-doc correction only).
- **Touches:** `execution_plan/execution_plan.md`.
- **Vertical slice:** REQ-ROLE-007a removed from P2-T4 REQ list; reassigned to an existing or new Phase 2 task whose scope includes the admin dashboard and job-runs infrastructure. If no suitable existing task is found, a new task is created at the end of Phase 2 with REQ-ROLE-007a in its REQ list.
- **Verification:** grep confirms REQ-ROLE-007a does not appear in P2-T4's REQ line after this task completes.

### P2-T5 — Admin step-up re-authentication helper + audit chain
- **Depends on:** P2-T4-CR.
- **REQ:** REQ-SEC-011, REQ-CONFIG-005a.
- **Vertical slice:** an action requiring step-up returns "re-authenticate" if the last OAuth handshake is older than 5 minutes; step-up event recorded and chained to the gated action's audit row.

### P2-T6 — Account recovery: linked secondary OAuth identity + admin-assisted rebind
- **Depends on:** P2-T5.
- **REQ:** REQ-RECOVERY-001..006.
- **Vertical slice:** user links a second identity; either identity logs in to the same account; admin-assisted rebind through the portal with step-up; before/after audit row.

### P2-T7 — FYERS credential management + token lifecycle + dirty-token UX
- **Depends on:** P2-T2.
- **REQ:** REQ-AUTH-003/003a/004/005/006/007/008/009/010/014, REQ-SESSION-009.
- **Vertical slice:** admin and user FYERS token flows persist in `fyers_tokens`; secrets in Key Vault; dirty token blocks dependent flows and surfaces the modal; admin sees a non-blocking warning instead of a hard lock.

### P2-T8 — Latest-tab-wins PLD WebSocket session lease
- **Depends on:** P1-T6.
- **REQ:** REQ-SESSION-014.
- **Vertical slice:** opening a second live-chart tab evicts the first with close code 4001 and the prescribed banner; lease TTL `session.fyers.lease_ttl_seconds`; heartbeat at half TTL.

### P2-T9 — `sys_config` seeder console app + manifest + sentinel + per-deployment keys + secret-prohibition validator
- **Depends on:** P1-T6, P1-T11.
- **REQ:** REQ-CONFIG-009/010/011, REQ-CONFIG-004.
- **Vertical slice:** seeder inserts missing rows only; never overwrites; resolves per-deployment keys from pipeline params; refuses any key whose name matches a secret pattern; writes `platform.seed.version` sentinel; emits structured report artefact.
- **Frozen-after-author** for the seeder topology; manifest evolves with REQ additions.

### P2-T10 — Deployment workflow ordering + sentinel-row startup check
- **Depends on:** P2-T9.
- **REQ:** REQ-CONFIG-010, REQ-MIGRATION-002/004/006/007.
- **Vertical slice:** GitHub Actions runs FluentMigrator -> Mongo.Migration -> seeder -> API/Worker activation; API and Worker fail fast if `platform.seed.version` is absent; production deploy verifies migrations against a restored snapshot.
- **Frozen-after-author.**

### P2-T11 — Admin `sys_config` management UI with audit chain + step-up gating
- **Depends on:** P2-T5, P2-T10.
- **REQ:** REQ-CONFIG-005/005a/007, REQ-SEC-011 (sys_config `risk` / `legal` / `operations` categories).
- **Vertical slice:** admin edits a non-sensitive key; audit row written before the value is updated; sensitive-category edit blocked without step-up.

### P2-T12 — Admin approval flow + non-approved-user lock-out screens
- **Depends on:** P2-T3.
- **REQ:** REQ-ROLE-004, REQ-SESSION-012/013.
- **Vertical slice:** unapproved user sees the approval-pending screen; admin approves -> user is redirected through FYERS auth on next login; deactivated user sees the deactivated screen.

### P2-T13 — Versioned legal documents at stable URLs
- **Depends on:** P1-T11, P2-T2.
- **REQ:** REQ-LEGAL-007/008, REQ-LEGAL-004.
- **Vertical slice:** ToS, Privacy Policy, long-form disclaimer, Phase A tester acknowledgement live at fixed URLs; persistent footer links present on every page; current version stored in `sys_config.legal.*`.

### P2-T14 — Signup intake: three distinct affirmative checkboxes + minor self-declaration + bumped-version re-acceptance
- **Depends on:** P2-T13.
- **REQ:** REQ-LEGAL-004/008, REQ-PRIVACY-003/007.
- **Vertical slice:** signup requires three separate checkboxes; minor declaration is required; `audit_events` rows recorded; bumping any document version forces re-acceptance on next login before any protected page renders.

### P2-T15 — Privacy notice + RoPA + grievance officer page
- **Depends on:** P2-T13.
- **REQ:** REQ-PRIVACY-001/002/005/008/009/010/011/012.
- **Touches:** `docs/privacy/ropa.md`, `apps/web` footer page.
- **Vertical slice:** `docs/privacy/ropa.md` exists with all categories + retention windows; portal "Contact and Grievances" page lists the named officer; impersonation processing-activity entry present.

### P2-T16 — Operating phase recording in `sys_config` + audit
- **Depends on:** P2-T11.
- **REQ:** REQ-LEGAL-001.
- **Vertical slice:** `operations.phase.current` defaults to `A`; transition through admin UI requires step-up + justification + audit row.

### P2-T17 — REQ-ORDER-010b CNC sandbox verification gate
- **Depends on:** P2-T7.
- **REQ:** REQ-ORDER-010b.
- **Vertical slice:** minimal `<fyers-button>` page hosted; sandbox confirms `data-product = CNC` is accepted; outcome recorded as ADR note under `docs/adr/`; helper edits `status.json` to deactivate the inactive Phase 7 branch (P7 vs P7-FALLBACK).

### P2-T18 — Phase-gate verification for Phase 2
- **Depends on:** P2-T1..T17, P2-T4-CR.

---

## Phase P3 — Universe, Calendar, Market Data Provider (v0.4)

### P3-T1 — FYERS bulk-quotes rate-limit accounting verification
- **Depends on:** P2-T7.
- **REQ:** REQ-MARKET-002c, ADR-0005.
- **Touches:** `docs/operations/fyers-api-budget.md`.
- **Vertical slice:** verified accounting recorded in `fyers-api-budget.md` § LMDS; `jobs.live_market_scan.poll_interval_seconds` default adjusted if needed.
- **Blocks:** P3-T9, P3-T11..T14, P5-T12.

### P3-T2 — Symbol master + universe state + suffix generation + collision check at boot
- **Depends on:** P1-T11.
- **REQ:** REQ-UNIV-001..010, REQ-HIST-005/006/007/008/008a, `lot_size` from REQ-UNIV-002.
- **Vertical slice:** symbol_master CRUD; suffix generated deterministically; suffix-collision in symbol_master refuses Worker startup; `lot_size` defaults to 1.

### P3-T3 — Internal trading calendar + admin UI + IST canonical zone + session vs non-trading-day distinction
- **Depends on:** P2-T11.
- **REQ:** REQ-CALENDAR-001..006.
- **Vertical slice:** admin enters normal sessions, special sessions, Muhurat sessions, and explicit non-trading-day markers; IST start/end times stored.

### P3-T4 — Calendar coverage check + admin warning + Legal Posture widget hook
- **Depends on:** P3-T3.
- **REQ:** REQ-CALENDAR-007.
- **Vertical slice:** any unconfirmed weekday in the next 30 days surfaces a banner; reaches Legal Posture widget; gates A->B transition.

### P3-T5 — Universe Sync flow: validation, diff preview, rename detection, archive-confirm threshold, rollback
- **Depends on:** P2-T11, P3-T2.
- **REQ:** REQ-UNIV-011..020.
- **Vertical slice:** CSV upload -> diff with rename candidates by ISIN -> typed-confirmation when archives exceed `operations.universe.archive_confirm_threshold_pct` -> commit -> 30-day rollback control.

### P3-T6 — Universe upload page: live symbol master + out-of-sync indicator + reseed + auto-trigger HDS
- **Depends on:** P3-T5.
- **REQ:** REQ-UNIV-014/015a/015b/015c.
- **Vertical slice:** out-of-sync count visible; one-click reseed for all out-of-sync; auto-trigger HDS for net-new symbols on commit; in-flight progress.

### P3-T7 — Symbol Validity Probe + admin work queue + rename / delisting / dismiss resolutions
- **Depends on:** P3-T9, P3-T2.
- **REQ:** REQ-UNIV-021..021b.
- **Vertical slice:** probe runs after first daily admin token; per-symbol classification (Success / Transient / Unknown); flag at `operations.universe.symbol_probe_flag_threshold_days`; admin resolves with in-place rename / archive / dismiss.

### P3-T8 — MDP interface contract + REST-only constraint
- **Depends on:** P1-T11.
- **REQ:** REQ-MARKET-002a/002b, engineering-standards § MDP Standards, REQ-MKTPROV-005.
- **Vertical slice:** `IMarketDataProvider` exposes history + quote + push-adapter pattern; backend code that talks to a provider directly fails the build.

### P3-T9 — FYERS adapter + throttle layer + rate-limit budget
- **Depends on:** P3-T1, P3-T8.
- **REQ:** REQ-RATE-003/004/011/012.
- **Vertical slice:** all FYERS calls go through the throttler; per-second/minute/day budgets enforced; budget metric emits; 429 -> backoff.

### P3-T10 — Cross-provider swap test scaffolding
- **Depends on:** P3-T8, P3-T9.
- **REQ:** REQ-MARKET-002d.
- **Vertical slice:** stub TrueData adapter returns canned OHLCV; integration test swaps `market_data.provider.active` and asserts DataSync, EODSR, chart layer continue without code changes.

### P3-T11 — SQL Server historical schema + symbol-table mapping service + chart data endpoints
- **Depends on:** P3-T2, P3-T8.
- **REQ:** REQ-HIST-001..011, REQ-HIST-010a.
- **Vertical slice:** chart endpoint returns OHLCV via `ISymbolTableMapping`; weekly Muhurat-on-Saturday case verified by unit test.

### P3-T12 — HistoricDataSeed: scoped per-symbol table materialisation + idempotent + resumable
- **Depends on:** P3-T9, P3-T11.
- **REQ:** REQ-HIST-009, REQ-HIST-009a.
- **Vertical slice:** HDS creates `D_/W_/M_` tables for a new symbol atomically with `IF NOT EXISTS`; second run is no-op; resumable from last successful date.

### P3-T13 — DataSync (DS) job: 10-session recovery + weekly/monthly upserts + success marker + token re-check
- **Depends on:** P3-T12.
- **REQ:** REQ-MARKET-005/005a/006/007.
- **Vertical slice:** DS runs; backfills last 10 sessions; weekly/monthly candles upserted; admin token re-checked at every batch boundary; mid-run expiry -> `partial_completion_token_expired`; success marker written.

### P3-T14 — Live quote / portal live data: browser-tier FYERS WebSocket + PLD lease + REST fallback
- **Depends on:** P2-T8, P3-T9.
- **REQ:** REQ-MARKET-002b, REQ-DASH-013, REQ-STOP-006c.
- **Vertical slice:** chart page subscribes via browser WebSocket using user FYERS token; lease enforced; REST fallback works on disconnect; quote-as-of timestamp visible.

### P3-T15 — Phase-gate verification for Phase 3
- **Depends on:** P3-T1..T14.

---

## Phase P4 — Strategy Research and Backtesting (v0.5)

### P4-T1 — Signal Subscription model + versioned parameter sets + copy-on-write + pause/resume safety
- **Depends on:** P2-T3.
- **REQ:** REQ-STRAT-007a/007b, REQ-STRAT-017b.
- **Vertical slice:** create + edit + pause + resume; in-flight EODSR uses the version it started with; pause/resume mid-run does not affect the run.

### P4-T2 — Historical query layer + shared timeframe & calendar layer + rolling 3/5/7 candles
- **Depends on:** P3-T11, P3-T3.
- **REQ:** REQ-TIMEFRAME-001..006, REQ-CALENDAR-003.
- **Vertical slice:** the same shared module returns daily / weekly / monthly / rolling candles for charting and backtest.

### P4-T3 — Backtest engine + result storage + slippage/commission + R metrics + low-confidence flagging
- **Depends on:** P4-T2.
- **REQ:** REQ-STRAT-011b/023, portfolio-risk-guidelines § Backtesting.
- **Vertical slice:** backtest emits per-trade + portfolio metrics; explicit slippage and commission visible on the result; low-confidence flag below the configured threshold.

### P4-T4 — Signal Builder + Backtest Result screens + notional starting equity default
- **Depends on:** P4-T3.
- **REQ:** REQ-BTSTORE-006a (notional default + caveat banner), REQ-BTSTORE-003 (version pinning).
- **Vertical slice:** user creates a Subscription, runs a backtest, sees results; starting equity defaults to `strategies.backtest.default_starting_equity_inr`.

### P4-T5 — Phase-gate verification for Phase 4
- **Depends on:** P4-T1..T4.

---

## Phase P5 — Live Operations + Portfolio Analytics (v0.6)
*Entry precondition: ADR-0004 calibration note for corporate-action detection threshold is committed at `docs/adr/0004-calibration-note-YYYYMMDD.md`. Implemented as P5-T0 below.*

### P5-T0 — Commit ADR-0004 corporate-action detection threshold calibration note
- **Depends on:** P4-T5.
- **REQ:** ADR-0004.
- **Vertical slice:** calibration note committed and referenced from ADR-0004; sample window + detection code linked.

### P5-T1 — DataSync EOD success marker contract + EODSR sequencing gate
- **Depends on:** P3-T13.
- **REQ:** REQ-MARKET-007.
- **Vertical slice:** EODSR refuses to start without the marker; marker written exactly once per session.

### P5-T2 — EOD Signal Runner (EODSR): single-pass per-symbol read + provenance + atomic-run + db-retry
- **Depends on:** P5-T1, P4-T1.
- **REQ:** REQ-STRAT-013/013a/027/028, `eodsr.db_retry.max_seconds`.
- **Vertical slice:** EODSR reads each symbol once, evaluates all subscriptions against it; entry signals carry provenance fields; failed run marked `failed_for_review`.

### P5-T3 — Notifications collection schema + writer paths from EODSR/LMDS/RME
- **Depends on:** P1-T11.
- **REQ:** REQ-NOTIFY-006.
- **Vertical slice:** all notification types enumerated and accepted by the schema; write from each producer covered by tests.

### P5-T4 — Notification Delivery Job (NDJ) — sole Telegram dispatcher + Retry-After + dirty-link auto-disable + global-disable break-glass
- **Depends on:** P5-T3.
- **REQ:** REQ-NOTIFY-008/019/022a.
- **Vertical slice:** NDJ delivers; honours `Retry-After` on 429; auto-disables Telegram link after `notifications.telegram.dirty_link_failure_threshold` failures; `notifications.telegram.global_disable=true` skips all dispatch.

### P5-T5 — Telegram bot provisioning + deep-link subscription + bot token rotation
- **Depends on:** P5-T4, P2-T5.
- **REQ:** REQ-NOTIFY-015/016/017/022a.
- **Vertical slice:** admin provisions bot; user subscribes via deep link with `notifications.telegram.linking_token_expiry_minutes` token; rotation requires step-up; previous bot record retained.

### P5-T6 — Portal notification feed (filter, unread, deep links, failed-delivery flag)
- **Depends on:** P5-T3.
- **REQ:** REQ-NOTIFY-014 (critical + total counts), REQ-PROFILE-006.
- **Vertical slice:** filter, unread split, deep links to chart, failed-delivery flag.

### P5-T7 — LADS skeleton + sustained-failure suspension
- **Depends on:** P2-T7, P3-T9.
- **REQ:** REQ-PORT-018..021a.
- **Vertical slice:** LADS polls per user at `jobs.account_sync.intraday_interval_minutes`; three consecutive aborts trigger graduated suspension and admin advisory.

### P5-T8 — Per-user trade-ledger write lock wired into all ledger writers
- **Depends on:** P1-T13, P5-T7.
- **REQ:** REQ-PORT-031 + REQ-PORT-031a + REQ-PORT-031b (full wire-up + U-8 manual-adjustment paths).
- **Vertical slice:** LADS, on-login sync, EOD sync, manual sync, admin rebuild, single-symbol backfill, and manual adjustment all acquire the lock; `ledger_snapshot_version` increments only after durable commit.

### P5-T9 — Immutable trade ledger ingestion + non-Nifty-500 exclusion
- **Depends on:** P5-T8.
- **REQ:** REQ-PORT-005a, REQ-RECON-001..004.
- **Vertical slice:** trades append-only and idempotent; non-Nifty-500 symbols silently ignored.

### P5-T10 — Reconciliation Phase A subset
- **Depends on:** P5-T9.
- **REQ:** REQ-RECON-001..004.
- **Vertical slice:** auto-sync failure handling; defined recovery order; no-silent-edit; MongoDB adjustment-entry recording.

### P5-T11 — Holdings + PnL + reconciliation views
- **Depends on:** P5-T9.
- **REQ:** REQ-DASH-* (holdings/PnL subset).

### P5-T12 — Live Market Data Scan (LMDS) — continuous market-hours job
- **Depends on:** P3-T9, P3-T13, P1-T12.
- **REQ:** REQ-STOP-006/006a/006b/006c, REQ-SLO-004/010/011, REQ-PLC-004 (circuit-clear re-evaluation).
- **Vertical slice:** LMDS polls the configured interval during market hours; bulk-quote calls; per-position level evaluation; quote-delay indicator; capacity warning at `jobs.lmds.capacity_warn_pct`; calendar-aware startup; circuit-clear re-evaluates against first post-clear tick; uses ADR-0003 channel registry to enqueue events.

### P5-T13 — WebSocket fan-out infrastructure + degraded-Redis fallback
- **Depends on:** P5-T6, P5-T12.
- **REQ:** REQ-NFR-013, REQ-DATA-006a.
- **Vertical slice:** stop-breach + add/reduce + pending-entry-supersede push events reach connected clients; with Redis down, banner surfaces and clients fall back to polling.

### P5-T14 — Pre-market admin FYERS token validity check at 08:30 IST
- **Depends on:** P2-T7.
- **REQ:** REQ-NOTIFY-022, REQ-NOTIFY-022b.

### P5-T15 — MDP failure mode: shared-ingestion token loss
- **Depends on:** P5-T12, P3-T13.
- **REQ:** REQ-MARKET-016.
- **Vertical slice:** with admin FYERS token unavailable, LMDS / DS / EODSR suspend; user-token paths continue; admin banner + one-time Telegram alert fire.

### P5-T16 — Honour universe archive + scan-exclusion in EODSR + LMDS + `symbol_archived_with_open_position` notification
- **Depends on:** P5-T2, P5-T12.
- **REQ:** REQ-UNIV-015.

### P5-T17 — Phase-gate verification for Phase 5
- **Depends on:** P5-T0..T16.

---

## Phase P6 — Risk Management Engine (v0.7)

### P6-T1 — Authoritative state-transition matrix
- **Depends on:** P5-T17.
- **REQ:** REQ-PLC-002a.
- **Touches:** `docs/requirements-spec.md` (or sub-doc).
- **Vertical slice:** matrix lists every (from-state, event, to-state, source, reason-code); contract tests can be enumerated from it.

### P6-T2 — RME module skeleton + DI scaffolding + interface contracts
- **Depends on:** P6-T1, P5-T2, P5-T7, P5-T12.
- **REQ:** REQ-RME-001..005.
- **Vertical slice:** RME hosted in Worker; sizing / stop / advisory interfaces compile; no logic yet.

### P6-T3 — Position lifecycle state machine + transition validators per the matrix
- **Depends on:** P6-T2.
- **REQ:** REQ-PLC-002, REQ-PLC-005a.
- **Vertical slice:** every matrix row exercised by a unit test.

### P6-T4 — Position document `_version` OCC + filter-based writes + REQ-PORT-031b fencing-token integration
- **Depends on:** P6-T3, P5-T8.
- **REQ:** REQ-RME-CONC-002, REQ-PORT-031b.
- **Vertical slice:** concurrent write to a position with stale version retries; fencing-token abort never advances `ledger_snapshot_version`.

### P6-T5 — RME event types + per-position channel dispatch wired to the registry
- **Depends on:** P6-T4, P1-T12.
- **REQ:** REQ-RME-CONC-001..005.
- **Vertical slice:** LMDS / LADS / EODSR / EOD-trailing-stop produce events that route through the registry to the per-position consumer; trailing-stop carve-out enforced (rejected on the channel and run synchronously pre-session).

### P6-T6 — Snapshot-consistent equity-base read + manual override (capped)
- **Depends on:** P6-T5.
- **REQ:** REQ-RME-006a/006b/006c/006d (S-10 cap).
- **Vertical slice:** sizing reads equity at a consistent snapshot; override is capped; override-active retry message reads "Your equity override is in effect…".

### P6-T7 — Equity-base divergence advisory
- **Depends on:** P6-T6.
- **REQ:** REQ-RME-006e.
- **Vertical slice:** divergence beyond `risk.equity_base.external_divergence_warn_pct` produces dashboard card + one-time `equity_base_divergence_advisory` Telegram.

### P6-T8 — Sizing interface + Fixed % default
- **Depends on:** P6-T2.
- **REQ:** REQ-SIZING-005, portfolio-risk-guidelines § Position Sizing Models.

### P6-T9 — ATR-based sizing model
- **Depends on:** P6-T8.
- **REQ:** portfolio-risk-guidelines § Position Sizing Models.

### P6-T10 — Portfolio Heat-based sizing model
- **Depends on:** P6-T8.
- **REQ:** portfolio-risk-guidelines § Position Sizing Models.

### P6-T11 — Drawdown-adjusted sizing model
- **Depends on:** P6-T8.
- **REQ:** portfolio-risk-guidelines § Position Sizing Models.

### P6-T12 — Stop interface + Fixed Stop
- **Depends on:** P6-T2.
- **REQ:** portfolio-risk-guidelines § Stop Loss Types.

### P6-T13 — ATR Stop
- **Depends on:** P6-T12.

### P6-T14 — Trailing Stop (percentage)
- **Depends on:** P6-T12.

### P6-T15 — Trailing Stop (ATR) with EOD carve-out per ADR-0003
- **Depends on:** P6-T12.

### P6-T16 — Swing Low Stop
- **Depends on:** P6-T12.

### P6-T17 — Break-Even Stop (default `risk.stop.breakeven_trigger_r`)
- **Depends on:** P6-T12.

### P6-T18 — Time Stop with calendar-aware date + post-calendar-edit recompute job
- **Depends on:** P6-T12, P3-T3.
- **REQ:** REQ-STOP-003/003a; default from `risk.time_stop.default_sessions`.
- **Vertical slice:** Time Stop date computed on entry; calendar edit triggers recompute job + per-position audit; one admin summary notification.

### P6-T19 — Portfolio heat calculator + maximum enforcement + correlated-industry grouping
- **Depends on:** P6-T8.
- **REQ:** REQ-HEAT-002, REQ-SIZING-014a.

### P6-T20 — Drawdown tracker + advisory vs enforcement split + renamed sys_config keys
- **Depends on:** P6-T19.
- **REQ:** REQ-DRDN-002/003/004.

### P6-T21 — Pyramiding + add/reduce + z-score overlay + never-lowers-stop floor
- **Depends on:** P6-T19, P6-T12.
- **REQ:** REQ-PYR-003/011, REQ-SIZING-007.

### P6-T22 — Pre-trade portfolio impact panel
- **Depends on:** P6-T19.
- **REQ:** portfolio-risk-guidelines § Required Auditability.

### P6-T23 — RME profile concept + RME profile optimisation backtests with shared SLO cap
- **Depends on:** P6-T20.
- **REQ:** REQ-RME-020/020a/020b/020c/021, REQ-SLO-007 + U-9.

### P6-T24 — Wire RME into backtest + live + nightly BTE-vs-RME parity
- **Depends on:** P4-T3, P5-T2, P6-T23.
- **REQ:** REQ-RME-003, REQ-NFR-017.
- **Vertical slice:** scheduled GitHub Actions workflow runs the parity fixture nightly; divergence fails the workflow + emits admin Telegram + email + audit row; `operations.bte_rme_parity.enabled` toggle works.

### P6-T25 — Admin global kill switch + per-user / per-strategy enable/disable + ADMIN-source suspension reasons
- **Depends on:** P6-T3.
- **REQ:** REQ-ADMIN-007/010/011/013, REQ-PLC-005a, REQ-ADMIN-016.

### P6-T26 — `rme_incidents` collection + admin incident dashboard + frozen-with-visibility resolution
- **Depends on:** P6-T4.
- **REQ:** REQ-RME-CONC-007.

### P6-T27 — Portal: full RME advisory panel + positions summary + portfolio health strip + idle-channel cap warning
- **Depends on:** P6-T22, P5-T13.
- **REQ:** REQ-CHART-* (advisory subset), RME-L1.

### P6-T28 — Dashboard: portfolio summary + XIRR + period performance + sector breakdown + portfolio-level freshness
- **Depends on:** P5-T11.
- **REQ:** REQ-DASH-012, T-9.

### P6-T29 — Telegram entry-signal + exit-advisory templates
- **Depends on:** P5-T5.
- **REQ:** REQ-NOTIFY-024/025.

### P6-T30 — Phase-gate verification for Phase 6
- **Depends on:** P6-T1..T29.

---

## Phase P7 — Execution Assistance (v0.8) — conditional on REQ-ORDER-010b pass
*Entered only if `P2-T17` outcome is **pass**. Otherwise execute P7-FALLBACK below.*

### P7-T1 — Embed FYERS API Connect SDK + URL pin + SHA-256 hash pin + contract-suite trigger on change
- **Depends on:** P2-T17.
- **active_when:** ADR REQ-ORDER-010b verification recorded as `pass`.
- **REQ:** REQ-ORDER-016a, REQ-ORDER-006/010.

### P7-T2 — Frontend ↔ API contract test suite (turn the P1-T9 scaffolding on)
- **Depends on:** P7-T1.
- **active_when:** as above.
- **REQ:** REQ-NFR-016.

### P7-T3 — Pre-flight check endpoint
- **Depends on:** P7-T2, P6-T19, P6-T20, P6-T25, P5-T7.
- **active_when:** as above.
- **REQ:** token validity + kill switch + drawdown enforcement (REQ-DRDN-003 (a)) + duplicate-in-flight + halt state (REQ-HALT-005) + LADS sustained-failure (RME-R2).

### P7-T4 — Phase 1 platform modal: Entry / Add / Reduce / Exit + CNC enforced + quote auto-refresh + halt warning
- **Depends on:** P7-T3, P3-T14.
- **active_when:** as above.
- **REQ:** REQ-ORDER-007/008/011 (CG-7), REQ-HALT-005, REQ-ORDER-018.

### P7-T5 — Signed-payload endpoint + multi-intent heat gate + LADS health gate + visibility-change refresh listener
- **Depends on:** P7-T4.
- **active_when:** as above.
- **REQ:** REQ-ORDER-009b/009c, EC-5, RME-R2.

### P7-T6 — `intent_ledger` collection + indexes + nonce uniqueness + intent write before widget activation
- **Depends on:** P7-T5.
- **active_when:** as above.
- **REQ:** REQ-ORDER-014a/014b.

### P7-T7 — `<fyers-button>` rendering + global `finished` callback handler
- **Depends on:** P7-T6.
- **active_when:** as above.
- **REQ:** REQ-ORDER-015/015e/015f.
- **Vertical slice:** callback updates intent only; never advances position lifecycle; success / failure / exception paths all covered.

### P7-T8 — LADS intent-reconciliation safety net
- **Depends on:** P7-T7, P5-T7.
- **active_when:** as above.
- **REQ:** REQ-ORDER-015c (callback-matched, callback-missed, unresolved at `orders.intent_timeout_minutes`, orphan_ack).

### P7-T9 — Awaiting-confirmation portal state + LADS-gated user notifications
- **Depends on:** P7-T8.
- **active_when:** as above.
- **REQ:** REQ-ORDER-015e/015f.

### P7-T10 — Callback reliability metrics + admin alert at `orders.callback_failure_alert_ratio`
- **Depends on:** P7-T7.
- **active_when:** as above.
- **REQ:** REQ-ORDER-015d.

### P7-T11 — Persistent "Exit position" affordance on positions summary
- **Depends on:** P7-T4.
- **active_when:** as above.
- **REQ:** T-8 / REQ-STOP-008 amendment.

### P7-T12 — Contract test suite (10 tests per roadmap Phase 7 list)
- **Depends on:** P7-T7, P7-T8, P7-T11.
- **active_when:** as above.
- **REQ:** REQ-ORDER-010c, REQ-ORDER-016a.

### P7-T13 — Phase-gate verification for Phase 7
- **Depends on:** P7-T1..T12.
- **active_when:** as above.

## Phase P7-FALLBACK — Execution Assistance Fallback (v0.8) — conditional on REQ-ORDER-010b fail

### P7F-T1 — Wire REQ-ORDER-017/017a deep-link handoff into all action surfaces
- **Depends on:** P2-T17, P6-T27.
- **active_when:** REQ-ORDER-010b verification recorded as `fail`.
- **REQ:** REQ-ORDER-017/017a/019.

### P7F-T2 — Verify RME advisory + Telegram + analytics + dashboard remain unchanged in fallback mode
- **Depends on:** P7F-T1.
- **active_when:** as above.

### P7F-T3 — Visible product-identity note + REQ-NEXT-011 reference
- **Depends on:** P7F-T1.
- **active_when:** as above.

### P7F-T4 — Phase-gate verification for fallback
- **Depends on:** P7F-T1..T3.
- **active_when:** as above.

---

## Phase P8 — Admin Operations, Hardening, Corporate Actions (v0.9)

### P8-T1 — `audit_events` collection + helper APIs + retention
- **Depends on:** P1-T11.
- **REQ:** data-management.md `audit_events`.

### P8-T2 — Admin job monitoring + retry controls + Symbol Validity Probe banner + maintenance window display
- **Depends on:** P5-T2, P3-T7.
- **REQ:** REQ-ADMIN-014, A-14.

### P8-T3 — Operating phase transition workflow + gating + SEBI classification opinion gate
- **Depends on:** P2-T16, P3-T4.
- **REQ:** REQ-LEGAL-001/005/005a.

### P8-T4 — Legal Posture widget on admin home page
- **Depends on:** P8-T3.
- **REQ:** REQ-LEGAL-010.

### P8-T5 — Admin "View as user" impersonation
- **Depends on:** P2-T5.
- **REQ:** REQ-ADMIN-015, REQ-PRIVACY-012, `operations.impersonation.idle_timeout_minutes`.

### P8-T6 — Admin user management + deactivate / reactivate + per-user signal suspension + manual position suspend/release
- **Depends on:** P2-T12.
- **REQ:** REQ-ADMIN-001a/001b/010, REQ-PLC-005a (`ADMIN` source).

### P8-T7 — Phase A mandatory runbooks authored
- **Depends on:** P5-T17, P6-T26.
- **REQ:** REQ-LEGAL-005.
- **Touches:** `docs/operations/runbooks/`.
- **Vertical slice:** runbooks #1, #2, #3 refined; #4 (RME concurrency-freeze resolution) and #5 (MongoDB failover / LADS recovery) authored.

### P8-T8 — Annual restore drill + first execution recorded
- **Depends on:** P1-T10.
- **REQ:** REQ-BCP-007.

### P8-T9 — WCAG 2.1 AA manual audit before Phase B
- **Depends on:** P6-T28.
- **REQ:** REQ-ACCESS-001.

### P8-T10 — Penetration test scheduling + remediation tracking (Phase C gate)
- **Depends on:** P8-T3.
- **REQ:** REQ-SEC-010.

### P8-T11 — Load test at current `operations.phase_a.tester_ceiling`
- **Depends on:** P5-T17, P6-T30.
- **REQ:** engineering-standards § Load-test SLO targets, REQ-NFR-014.

### P8-T12 — Chaos / failure-injection exercises (Phase C gate)
- **Depends on:** P8-T11.
- **REQ:** REQ-NFR-014.

### P8-T13 — Corporate actions + symbol continuity
- **Depends on:** P5-T0, P5-T9.
- **REQ:** REQ-NEXT-* corporate-action subset (per legacy roadmap Phase 9).

### P8-T14 — Phase-gate verification for Phase 8
- **Depends on:** P8-T1..T13.

---

## Phase P9 — V1 Hardening Pass (v1.0)

### P9-T1 — Regression + E2E coverage gate for every critical workflow
- **Depends on:** P8-T14.
- **REQ:** engineering-standards § Testing Standards.

### P9-T2 — RME unit-test 95% + contract-test-per-state-transition floor
- **Depends on:** P6-T30.
- **REQ:** REQ-NFR-014, engineering-standards § Testing Standards.

### P9-T3 — Final phase-gate verification + V1 release-readiness ADR
- **Depends on:** P9-T1, P9-T2.

---

## Notes for the agent

- A task is complete only when its **Vertical slice** is demonstrable and every REQ-ID listed under **REQ** is observably satisfied.
- A task whose dependencies are not in `completed_tasks` must be deferred per `agent.md` step 2.
- A task with a false `active_when` predicate is moved to `deactivated_tasks` per `agent.md` step 3.
- A task that uncovers a missing REQ or contradiction must stop and create a conflict-resolution task (new ID), per `conventions.md` § cross-agent rules.
- Every task tagged **Frozen-after-author** must not be re-opened in later phases without an explicit ADR amendment.
