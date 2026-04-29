# Milestones (Versioned Delivery Checkpoints)

Versions are **delivery checkpoints**. A version is "shippable" when its acceptance criteria are met, the included tasks are complete in `execution_plan/status.json`, and the phase-gate task at the end of the phase has run successfully.

Plan revision applied 2026-04-25 per `docs/build_docs_reviews/execution_plan_review_2026_04_25.md`.

## v0.1 — Versioned Execution Plan System

- **Tasks:** P0-T1, P0-T2, P0-T3.
- **Acceptance:**
  - All required execution-plan artefacts exist (`execution_plan.md`, `milestones.md`, `conventions.md`, `agent.md`, `status.json`, `task_logs/`).
  - `agent.md` enforces REQ-ID coverage, conflict-resolution branching, phase-gate verification, and Frozen-after-author handling.
  - `status.json` carries `phase_coverage` and `deactivated_tasks` fields.
  - An agent can deterministically identify and resume the next task using `status.json`.

## v0.2 — Foundation, SRE Scaffolding, Data Foundation

- **Tasks:** P1-T1..T14.
- **Acceptance:**
  - Every Phase 1 task is committed and marked Frozen-after-author.
  - Worker singleton enforcement layers (IaC, pipeline gate, Redis lease) are demonstrably active (REQ-RME-CONC-006).
  - OTLP collector pair + saturation policy + fallback endpoint are wired (REQ-NFR-008/009/009a, ADR-0002).
  - App Configuration + Key Vault bootstrap with LKG cache and staleness alert are wired (REQ-CONFIG-001/004/006/008, ADR-0002).
  - Serilog redaction policy is unit-tested (engineering-standards § Security Standards).
  - Security baseline (CSRF, CSP, headers, WAF, rate limiting) is active on every portal response (REQ-SEC-001..006).
  - CI gates for dep scan, SAST, secret scan, axe-core, contract-diff scaffolding, coverage thresholds are enforcing (REQ-SEC-007/008/009, REQ-NFR-014/016, REQ-ACCESS-001).
  - BCP / backup configuration is provisioned and verifiable per RPO/RTO (REQ-BCP-001..008, REQ-LEGAL-009).
  - Every collection in `data-management.md` is provisioned with prescribed indexes, TTL, and Online Archive policies (REQ-DATA-001..006a).
  - ADR-0003 per-position channel registry skeleton exists with serialised-write unit test.
  - Per-user trade-ledger lock primitives exist with fencing token and snapshot-version helper (REQ-PORT-031 primitives only).
  - Phase 1 phase-gate verification has run.

## v0.3 — Identity, Sessions, Legal/Privacy, Admin Bootstrap, sys_config

- **Tasks:** P2-T1..T18.
- **Acceptance:**
  - OAuth + Bearer-JWT auth boundary works end-to-end with all three providers (REQ-AUTH-001/002/011).
  - Server-side `sessions` collection enforces 24h TTL + single-active-session + FYERS-required redirect + expiry warning (REQ-SESSION-001..013).
  - Phase A tester ceiling enforced at approve-time (REQ-LEGAL-003).
  - Lazy admin bootstrap + admin OAuth restricted to Google / Microsoft + `amr` MFA enforcement (REQ-ROLE-005, REQ-BCP-009).
  - Admin step-up re-auth gates the 13 destructive actions (REQ-SEC-011).
  - Account recovery (linked secondary identity + admin-assisted rebind with step-up) works (REQ-RECOVERY-001..006).
  - FYERS credential management + dirty-token UX + admin-token banner work (REQ-AUTH-003..010, REQ-SESSION-009).
  - Latest-tab-wins PLD lease enforced (REQ-SESSION-014).
  - `sys_config` seeder + sentinel + per-deployment keys + secret-prohibition validator + sentinel-row startup check work (REQ-CONFIG-009/010/011, REQ-CONFIG-004).
  - Admin `sys_config` UI with audit chain and step-up gating works (REQ-CONFIG-005/005a/007).
  - Versioned legal documents + three-checkbox signup + minor declaration + bumped-version re-acceptance work (REQ-LEGAL-004/007/008, REQ-PRIVACY-003/007).
  - Privacy notice + RoPA + grievance officer page exist (REQ-PRIVACY-001/002/005/008/009/010/011/012).
  - Operating phase recording in `sys_config` + transition workflow gating (REQ-LEGAL-001).
  - REQ-ORDER-010b CNC sandbox verification outcome recorded; P7 vs P7-FALLBACK selected.
  - Phase 2 phase-gate verification has run.

## v0.4 — Universe, Calendar, Market Data Provider

- **Tasks:** P3-T1..T15.
- **Acceptance:**
  - FYERS bulk-quotes accounting verified and recorded; `jobs.live_market_scan.poll_interval_seconds` adjusted if needed (REQ-MARKET-002c, ADR-0005).
  - Symbol master + suffix generation + collision check at boot work; `lot_size` field present (REQ-UNIV-001..010, REQ-HIST-005..008a).
  - Internal trading calendar admin UI works; sessions vs explicit non-trading-day distinction enforced (REQ-CALENDAR-001..006).
  - Calendar coverage check fires + reaches Legal Posture widget + gates A->B (REQ-CALENDAR-007).
  - Universe Sync diff preview + rename detection + archive-confirm threshold + 30-day rollback work (REQ-UNIV-011..020).
  - Universe upload page shows live symbol master + out-of-sync indicator + auto-trigger HDS for net-new (REQ-UNIV-014..015c).
  - Symbol Validity Probe + admin work queue + rename / delisting / dismiss resolutions work (REQ-UNIV-021..021b).
  - MDP interface + REST-only constraint enforced; FYERS adapter + throttle layer + budget metric live (REQ-MARKET-002a/002b, REQ-RATE-003/004/011/012, engineering-standards § MDP).
  - Cross-provider swap test scaffolding works against a stub TrueData adapter (REQ-MARKET-002d).
  - SQL Server historical schema + symbol-table mapping service + chart endpoints serve OHLCV (REQ-HIST-001..011, REQ-HIST-010a).
  - HistoricDataSeed scoped per-symbol table materialisation + idempotent + resumable (REQ-HIST-009/009a).
  - DataSync 10-session recovery + weekly/monthly upserts + success marker + token re-check (REQ-MARKET-005/005a/006/007).
  - Browser-tier FYERS WebSocket for live quotes + PLD lease + REST fallback (REQ-MARKET-002b, REQ-DASH-013, REQ-STOP-006c).
  - Phase 3 phase-gate verification has run.

## v0.5 — Strategy Research and Backtesting

- **Tasks:** P4-T1..T5.
- **Acceptance:**
  - Signal Subscription versioning + copy-on-write + pause/resume mid-EODSR safety (REQ-STRAT-007a/007b/017b).
  - Shared timeframe + calendar layer used by chart + backtest + EODSR (REQ-TIMEFRAME-001..006, REQ-CALENDAR-003).
  - Backtest engine + R metrics + slippage/commission + low-confidence flag (REQ-STRAT-011b/023).
  - Signal Builder + Backtest Result screens use notional starting equity default (REQ-BTSTORE-006a).
  - Phase 4 phase-gate verification has run.

## v0.6 — Live Operations + Portfolio Analytics

- **Entry precondition:** ADR-0004 calibration note committed (P5-T0).
- **Tasks:** P5-T0..T17.
- **Acceptance:**
  - DataSync success marker contract + EODSR sequencing gate (REQ-MARKET-007).
  - EODSR single-pass per-symbol read + provenance + atomic-run + db-retry (REQ-STRAT-013/013a/027/028).
  - Notification store + NDJ as sole Telegram dispatcher + Retry-After + dirty-link auto-disable + global-disable break-glass (REQ-NOTIFY-006/008/019/022a).
  - Telegram bot provisioning + deep-link subscription + token rotation (REQ-NOTIFY-015/016/017/022a).
  - Portal feed + critical-vs-total unread counts (REQ-NOTIFY-014, REQ-PROFILE-006).
  - LADS skeleton + sustained-failure suspension (REQ-PORT-018..021a).
  - Per-user trade-ledger lock wired into all writers including manual adjustments (REQ-PORT-031 + U-8).
  - Immutable trade ledger + non-Nifty-500 exclusion (REQ-PORT-005a, REQ-RECON-001..004).
  - LMDS continuous market-hours + per-position level evaluation + capacity warning + circuit-clear re-eval (REQ-STOP-006/006a/006b/006c, REQ-SLO-004/010/011, REQ-PLC-004).
  - WebSocket fan-out + degraded-Redis fallback (REQ-NFR-013, REQ-DATA-006a).
  - Pre-market admin token check (REQ-NOTIFY-022b).
  - MDP failure-mode behaviour for shared-ingestion token loss (REQ-MARKET-016).
  - Universe archive + scan-exclusion honoured in EODSR + LMDS; `symbol_archived_with_open_position` notification (REQ-UNIV-015).
  - Phase 5 phase-gate verification has run.

## v0.7 — Risk Management Engine

- **Entry precondition:** state-transition matrix (REQ-PLC-002a) committed as P6-T1.
- **Tasks:** P6-T1..T30.
- **Acceptance:**
  - All sizing models, stop types, heat, drawdown, pyramiding, advisory generation, lifecycle states, and ADMIN-source suspension reasons implemented per `requirements-spec.md` and `portfolio-risk-guidelines.md`.
  - Per-position channel registry wired with EOD trailing-stop carve-out enforced; OCC + fencing-token integration complete (REQ-RME-CONC-001..005, REQ-PORT-031b).
  - `rme_incidents` collection + admin incident dashboard + frozen-with-visibility resolution + runbook #4 referenced (REQ-RME-CONC-007).
  - RME shared between live and backtest with no divergence; nightly BTE-vs-RME parity check active (REQ-RME-003, REQ-NFR-017).
  - Equity-base override capped + divergence advisory live (REQ-RME-006c..006e, S-10).
  - Portal RME advisory panel + dashboard portfolio-level freshness + Telegram entry + exit templates (REQ-NOTIFY-024/025, T-9).
  - RME unit-test coverage at 95% on the RME codebase + one contract test per state-transition matrix row (REQ-NFR-014).
  - Phase 6 phase-gate verification has run.

## v0.8 — Execution Assistance (conditional)

- **Entry condition:** REQ-ORDER-010b verification passed in P2-T17.
- **Tasks:** P7-T1..T13 (otherwise P7F-T1..T4).
- **Acceptance (P7 path):**
  - SDK URL pin + SHA-256 hash pin + contract-suite trigger on change (REQ-ORDER-016a).
  - Frontend <-> API contract test gate active (REQ-NFR-016).
  - Pre-flight + signed payload (HMAC + nonce + multi-intent heat gate + LADS health gate + visibility-change refresh) (REQ-ORDER-009b/009c, EC-5, RME-R2).
  - `intent_ledger` + intent-write-before-widget + capture strategy chosen (REQ-ORDER-014a/014b).
  - Callback handler updates intent only; never advances position lifecycle (REQ-ORDER-015/015e/015f).
  - LADS reconciliation safety net (callback-matched / callback-missed / unresolved / orphan_ack) (REQ-ORDER-015c).
  - Awaiting-confirmation portal state + LADS-gated user notifications (REQ-ORDER-015e/015f).
  - Callback reliability metric + admin alert (REQ-ORDER-015d).
  - Persistent "Exit position" affordance (T-8).
  - Phase 7 contract test suite (10 tests) green.
  - Phase 7 phase-gate verification has run.
- **Acceptance (P7-FALLBACK path):**
  - REQ-ORDER-017/017a deep-link handoff wired into every action surface (REQ-ORDER-019).
  - RME / Telegram / analytics / dashboard unaffected.
  - Visible product-identity note shipped; REQ-NEXT-011 referenced.
  - Phase 7-FALLBACK phase-gate verification has run.

## v0.9 — Admin Operations, Hardening, Corporate Actions

- **Tasks:** P8-T1..T14.
- **Acceptance:**
  - `audit_events` collection + helpers + retention live.
  - Admin job monitoring + retry controls + Symbol Validity Probe banner + maintenance window display (REQ-ADMIN-014, A-14).
  - Operating phase transition workflow + gating + SEBI classification opinion gate (REQ-LEGAL-001/005/005a).
  - Legal Posture widget on admin home page (REQ-LEGAL-010).
  - Admin "View as user" impersonation (REQ-ADMIN-015, REQ-PRIVACY-012).
  - Admin user management + per-user signal suspension + manual position suspend/release (REQ-ADMIN-001a/001b/010, REQ-PLC-005a `ADMIN` source).
  - Phase A mandatory runbooks #1..#5 authored (REQ-LEGAL-005).
  - Annual restore drill executed once (REQ-BCP-007).
  - WCAG 2.1 AA manual audit recorded (REQ-ACCESS-001) — Phase B gate.
  - Penetration test scheduled + tracking active (REQ-SEC-010) — Phase C gate.
  - Load test at current tester ceiling executed (REQ-NFR-014, engineering-standards Load-test SLO targets) — Phase B gate.
  - Chaos / failure-injection exercises (REQ-NFR-014) — Phase C gate.
  - Corporate actions + symbol continuity behaviour applied to charts / backtests / holdings.
  - Phase 8 phase-gate verification has run.

## v1.0 — V1 Hardening Pass

- **Tasks:** P9-T1..T3.
- **Acceptance:**
  - Critical workflows have integration + E2E coverage; regression coverage gate enforced.
  - RME 95% unit-test coverage + per-state-transition contract tests pass (REQ-NFR-014).
  - Final phase-gate verification + V1 release-readiness ADR committed.
