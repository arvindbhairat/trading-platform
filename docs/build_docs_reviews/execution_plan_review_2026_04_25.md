# Execution Plan Review — 2026-04-25

Reviewer: external system designer / architect / professional trader.
Scope: review and correct `execution_plan/execution_plan.md`, `milestones.md`, `agent.md`, `conventions.md`, `RUNBOOK.md`, and `status.json` against the canonical build docs (`docs/`) and the non-negotiable guardrails in `AGENTS.md` / `CLAUDE.md`. Do not invent requirements; only check that the plan is faithful, sequenced, and decomposable for AI-agent-driven execution.

A revised, clean execution plan is delivered alongside this review at `execution_plan/execution_plan_revised.md` (with companion `milestones_revised.md`). The current `execution_plan.md` is preserved per `conventions.md` ("do not rewrite wholesale"); the revised plan is offered as the proposed replacement once the changes below are accepted.

---

## 1. Executive Summary

The current execution plan is structurally sound — it adopts a deterministic resume model, follows the roadmap phase ordering, and respects most of the guardrails. The plan is, however, materially **incomplete and over-coarse** when measured against the canonical specs. It will not, as written, produce a correct platform if executed verbatim by an AI coding agent.

The dominant problems are:

- Several load-bearing components are completely missing from the plan, most notably the **Live Market Data Scan (LMDS)**, the **per-position event-serialisation channel registry from ADR-0003**, the **per-user trade-ledger write lock with fencing tokens (REQ-PORT-031/031a/031b)**, the **`sessions` collection and 24h portal-session lifecycle**, the **legal/privacy/DPDP intake flows that gate Phase A onboarding**, the **Symbol Validity Probe**, the **trading-calendar coverage check**, the **Universe Sync diff/preview/rollback**, and the **BCP/backup configuration**. These are not optional — most are explicit Phase A or Phase 0 obligations.
- Many tasks bundle several unrelated concerns into one item (e.g., "RME core module + plugin interfaces + state machine" in P7-T2; "Position sizing + stop mechanisms (pluggable)" in P7-T3). Each is too large to be an atomic, agent-executable, vertical slice.
- Several sequencing errors put work before its prerequisites — most importantly, the corporate-action calibration note is gated on RME entry rather than on Portfolio Analytics entry, and the FYERS bulk-quotes rate-limit verification (a Phase 2 precondition that may force a `sys_config` default change) is currently scheduled as a same-phase task with no enforcement that it precede market-data implementation.
- The plan does not enforce the **one-time-author-and-freeze** posture for Phase 0 SRE scaffolding that the roadmap now requires after the 2026-04-25 build-docs review. As written, agents can re-open singleton-lease wiring, OTLP saturation policy, and Key Vault bootstrap during product-feature phases.
- The plan has no explicit **coverage-check / requirements-traceability** mechanism. Tasks reference roadmap phases but not REQ-IDs, so an agent cannot verify whether a phase's REQ obligations have all been mapped to tasks.

The architecture is correct; the plan is the gap. None of these are fundamental defects, but the cumulative effect is that Phase A would ship without LMDS, without per-position serialisation, without legal acceptance flow, without WebSocket fan-out, and without backup configuration — none of which is acceptable for a platform that will touch user capital.

**Top risks if the plan is executed unchanged:**

1. **Silent RME concurrency corruption.** Without ADR-0003 channel registry scaffolding before P5/P7, LMDS and LADS will race on position documents. Optimistic concurrency alone (REQ-RME-CONC-002) detects collisions but does not serialise them; the channel registry is the load-bearing primitive.
2. **No live stop monitoring.** LMDS is missing from the plan. The platform would emit entry signals (P5) and a chart panel (P7) but never alert a user that a stop level has been breached intraday.
3. **Phase A onboarding cannot legally begin.** ToS, Privacy Policy, DPDP consent, RoPA, breach runbook, Phase A tester ceiling, Legal Posture widget, and operating-phase recording are all absent from the plan but mandatory per REQ-LEGAL/REQ-PRIVACY before the first non-admin user logs in.
4. **Trade-ledger corruption.** The per-user ledger write lock (REQ-PORT-031) is not in the plan; concurrent LADS, EOD sync, manual sync, and admin rebuild paths can corrupt FIFO state.
5. **Universe drift undetected.** Symbol Validity Probe, ISIN-based rename detection, and calendar-coverage check are all missing; the platform cannot reliably maintain its universe under NSE rebalancing.
6. **Tasks are not agent-executable.** Several tasks span 5+ subsystems (RME core; position sizing + stops; portfolio heat + drawdown + advisories) and will produce sprawling, untestable changes if handed to an agent as a single unit.

---

## 2. Findings

Findings are grouped by category, ranked by severity within each. Each item references the canonical doc(s) it is derived from. "P*-T*" identifiers refer to tasks in the current `execution_plan.md`.

### 2.1 Missing components (load-bearing)

**F-1. LMDS is absent from the plan (CRITICAL).**
The Live Market Data Scan is the worker job that monitors stop, add, and reduce levels for every open position during market hours using the shared admin FYERS token. It is named in `terminology.md`, `system-architecture.md` § Live Market Data Scan and § Worker Service Schedule, the EOD pipeline, ADR-0003, REQ-STOP-006/006a/006b/006c, REQ-NFR-013, REQ-SLO-004/010/011, and REQ-MARKET-016. It is referenced indirectly by P9 hardening but never built. **Add an explicit task under Live Operations.**

**F-2. ADR-0003 per-position channel registry is not scaffolded (CRITICAL).**
The `PositionChannelRegistry` (per-position `Channel<RmeEvent>`), the per-channel consumer task, and the Worker singleton lease are the load-bearing concurrency primitives. The plan only mentions the Redis singleton lease in P1-T4. The full ADR-0003 scaffolding — channel registry, channel-per-position lifecycle, EOD trailing-stop carve-out, idle-channel cap — must precede LMDS, LADS, and RME wiring. **Add a dedicated task in Phase 0 (or earliest Phase 5/6 prerequisite) before any code that writes to a position document.**

**F-3. Per-user trade-ledger write lock (REQ-PORT-031/031a/031b) is missing (CRITICAL).**
This Redis-backed lock with fencing token + `ledger_snapshot_version` increment is acquired by every ledger writer (LADS, on-login sync, EOD sync, manual sync, admin rebuild, single-symbol backfill, manual adjustments) and is the basis for the snapshot-consistent reads the RME relies on (REQ-RME-006d). Not implementing it produces silent FIFO corruption under any concurrent ledger write. **Add a task in Phase 5 (Portfolio Analytics) that lands the lock before any ledger writer is wired in.**

**F-4. Sessions collection + portal session lifecycle are missing.**
REQ-SESSION-001/002/002a/007/008/010/011/012/013 require a server-side `sessions` MongoDB collection with TTL index, single-active-session enforcement, expiry warning, redirect-on-expiry, pending-approval and FYERS-required state screens, and re-acceptance prompts when ToS/Privacy/disclaimer/acknowledgement versions bump. P2-T1 only states "OAuth sign-in." **Decompose into discrete tasks for the sessions collection, lock-out screens, expiry warning, FYERS redirect, and re-acceptance flow.**

**F-5. Legal, Privacy, DPDP intake is missing entirely.**
REQ-LEGAL-001/002/003/004/005/005a/006/007/008/009/010 and REQ-PRIVACY-001..012 mandate operating-phase recording, Phase A tester ceiling enforcement in the approval flow, three distinct affirmative checkboxes at signup (ToS, Privacy, Acknowledgement), versioned legal documents at stable URLs, persistent footer disclaimers, plain-language privacy notice, RoPA at `docs/privacy/ropa.md`, designated grievance officer, breach-procedure runbook (already Phase A mandatory), and the Legal Posture widget on the admin home page. None of these appear in P2 or P9. **All are pre-onboarding obligations; add them to Phase 1 (Identity) and Phase 8 (Admin/Hardening).**

**F-6. Symbol Validity Probe and ISIN-rename detection are missing.**
REQ-UNIV-020/021/021a/021b and the architecture's "Symbol Validity Probe Flow" require a daily probe of every active symbol via FYERS LTP, classification of failures into transient vs unknown-symbol, the admin work queue, and the rename-or-delisting resolution flow with the in-place suffix-preserving update. P3-T3 only covers "admin CSV upload with archive/exclusion handling." **Add the Symbol Validity Probe and the rename-detection diff path as separate tasks.**

**F-7. Universe Sync diff preview, archive-confirm threshold, and rollback are missing.**
REQ-UNIV-017/018/019/020 require a pre-commit diff with rename-candidate resolution, a typed-confirmation gate when archives exceed `operations.universe.archive_confirm_threshold_pct`, and a one-click 30-day rollback. P3-T3 does not call them out. **Add discrete tasks.**

**F-8. Trading calendar coverage check is missing.**
REQ-CALENDAR-002/005/006/007 require the admin to distinguish session records from explicit non-trading-day markers and require a rolling 30-day weekday coverage check that surfaces on the Legal Posture widget and is a phase-A→B transition gate. P3-T4 is silent on the coverage check. **Add as a sub-task.**

**F-9. `sql_table_name_suffix` collision check at boot is missing.**
REQ-HIST-008a requires the Worker to refuse to start if any duplicate suffixes exist. Add to P1-T4 (worker startup) and to the CSV upload validation path.

**F-10. Per-symbol table materialisation lifecycle (REQ-HIST-009a) is partially captured.**
P3-T7 mentions it; verify the task explicitly lists idempotent `IF NOT EXISTS`, per-symbol three-table transaction, shared DDL template (also used by REQ-MIGRATION-004), and `job_runs` audit event per creation.

**F-11. WebSocket fan-out infrastructure (REQ-NFR-013) is missing.**
Stop level breach alerts, add/reduce advisory triggers, and pending-entry supersede notifications must be pushed to connected portal clients via WebSocket using Redis as the fan-out layer. The plan has no task that establishes the WebSocket transport or wires Redis pub/sub. **Add a task in Live Operations after the notification store exists, before LMDS becomes user-visible.**

**F-12. PLD WebSocket browser-tier flow + latest-tab-wins eviction (REQ-SESSION-014) is missing.**
The browser-tier FYERS Data WebSocket for live quotes uses a Redis lease keyed by `session:fyers:pld:{user_id}` with eviction notification (close code 4001). The plan does not call this out. **Add to Phase 2 (Universe and Market Data) or Phase 3 (Strategy Research) once charting work begins.**

**F-13. BCP / backup configuration is missing.**
REQ-BCP-001..009 require MongoDB Atlas PITR (RPO 1h / RTO 2h), PostgreSQL market-data backups (RPO 24h / RTO 4h), PostgreSQL backtest backups (RPO 7d / RTO 8h), Key Vault soft-delete + 90-day retention + purge protection, geo-redundant replication to South India, restore procedures and an annual restore drill, secret-rotation cadence (FYERS yearly + on compromise; Telegram on-demand; Key Vault auto-rotated 90d; session signing keys 90d with 7-day overlap), and admin MFA validated via the `amr` claim with admin OAuth restricted to Google/Microsoft. None of this is in the plan. **Add to Phase 0 / Phase 1 as appropriate.**

**F-14. Security baseline (CSRF, CSP, security headers, WAF, rate limiting, dep scanning, SAST, secret scanning, Serilog redaction) is missing.**
REQ-SEC-001..013 and `engineering-standards.md` § Security Standards (token redaction in structured logs) require these as Phase A baselines. Only OAuth and FYERS-credential storage are mentioned in the plan. **Add to Phase 0 (CI gates, redaction policy, headers) and Phase 1 (CSRF tokens, rate limiting on auth endpoints, admin step-up re-auth).**

**F-15. Account recovery (REQ-RECOVERY-001..006) is missing.**
Linked secondary OAuth identity, max 2 linked identities, single-link warning, admin-assisted rebind with step-up + audit. Not in the plan. **Add to Phase 1.**

**F-16. Admin step-up re-authentication (REQ-SEC-011) is missing.**
The 5-minute fresh-OAuth gate in front of 13 admin actions (kill switch, deactivation, signal suspend, sys_config writes in `risk`/`legal`/`operations`, phase transition, Telegram bot rotation, FYERS app credential replacement, recovery, ledger rebuild, impersonation, manual position suspend/release) is not in the plan. **Add to Phase 1 (foundation) and reference in every admin-action task.**

**F-17. `sys_config` mutation audit chain (REQ-CONFIG-005a) is missing.**
The application-layer atomic-sequence write to `audit_events` before `sys_config` is not described. P2-T7 just says "view and edit." **Add explicitly.**

**F-18. Notification types and templates are not enumerated.**
REQ-NOTIFY-006 (notification types), REQ-NOTIFY-024 (entry-fill template), REQ-NOTIFY-025 (exit-advisory template), the suppressible/non-suppressible split (REQ-PROFILE-006), the dirty-link failure threshold (REQ-NOTIFY-019), and the global-disable break-glass flag (REQ-NOTIFY-022a) are all absent from P5-T3..T5. **Decompose.**

**F-19. Reconciliation safety net for intent ledger (REQ-ORDER-015c) is captured but the orphan-acknowledgement path is under-specified in P8-T5.**
Make explicit that LADS reconciliation, `unresolved` transition at `orders.intent_timeout_minutes`, `orphan_ack` advisory, and the LADS-only-ratio admin alert are separate validatable behaviours, not a single bullet.

**F-20. Frontend ↔ API contract test suite (REQ-NFR-016) is missing.**
This must be in place **before Phase 7** because the order placement flow is the high-risk surface. The plan says nothing about it. **Add to Phase 1 (CI scaffolding) with a hard gate before P8 begins.**

**F-21. BTE-vs-RME nightly parity check (REQ-NFR-017) is missing.**
GitHub Actions scheduled workflow + fixture + admin alert + `operations.bte_rme_parity.enabled` toggle. Phase 6 deliverable; gates Phase 7. **Add to Phase 6.**

**F-22. State-transition matrix (REQ-PLC-002a) is missing.**
The exhaustive (from-state, event, to-state, source, reason-code) tuple table that gates the RME contract-test floor (REQ-NFR-014, engineering-standards.md). **Add as a Phase 6 prerequisite documentation task.**

**F-23. Single-pass EODSR market-data read pattern (REQ-STRAT-013a) is not enforced.**
The execution plan task for EODSR (P5-T2) does not name the constraint. A naive implementation will cost 75,000 reads per run. **Add as a constraint in the task description.**

**F-24. Operating-phase transition workflow + Legal Posture widget + tester ceiling enforcement is missing.**
REQ-LEGAL-001/003/005/005a/010 require the admin UI for phase transition with hard-coded gating (legal review, runbook completeness, calendar coverage, SEBI classification opinion, etc.), and the Legal Posture widget on the admin home page with all the named display fields. Not present. **Add to Phase 8.**

**F-25. Admin "View as user" impersonation (REQ-ADMIN-015 / REQ-PRIVACY-012) is missing.**
Step-up gated, read-only enforcement with server-side write rejection, full audit trail, idle timeout. **Add to Phase 8.**

**F-26. `intent_ledger`, `rme_incidents`, `symbol_health`, `watchlists`, `notification_bots`, `manual_adjustments`, `equity_curve`, `audit_events`, `job_runs` collections are not provisioned in the plan.**
Each has prescribed indexes, lifecycle mechanism (TTL or Online Archive), and required fields per `data-management.md`. The plan only references the abstract notion of MongoDB models. **Add a task per collection (or a single task that lands the full data-management.md catalog) before the relevant feature work.**

**F-27. Admin Job Monitoring (P9-T1) is too coarse.**
Should be decomposed into: `job_runs` admin grid; per-job retry control; symbol-scoped reseed; HDS resume; EODSR retry; LMDS poll-interval adjuster; Universe Sync rollback; Symbol Validity Probe banner; manual position suspend/release; kill switch toggle. Several of these are explicit REQ-ADMIN items.

**F-28. WCAG 2.1 AA, browser smoke tests, screen-reader audit (REQ-ACCESS-001..007) are missing.**
Phase B audit + annual audit + axe-core in CI. **Add to Phase 0 (CI axe scan) and Phase 8 (manual audit).**

**F-29. Pre-market admin FYERS token check (REQ-NOTIFY-022b) is missing.**
A pre-market scheduled job at 08:30 IST that catches missing daily tokens before LMDS starts. **Add to Live Operations.**

**F-30. Telegram bot provisioning (REQ-NOTIFY-016/017/022a) is too thin in P5-T4.**
Decompose into: bot configuration (`notification_bots` collection); deep-link subscription token (`notifications.telegram.linking_token_expiry_minutes`); dirty-link auto-disable at threshold; bot token rotation flow with step-up; global-disable break-glass flag.

### 2.2 Sequencing and dependency errors

**F-31. Calibration note (P7-T1) is mis-placed.**
The roadmap states the corporate-action detection threshold calibration note (`docs/adr/0004-calibration-note-YYYYMMDD.md`) must be committed **before Phase 5 implementation begins** (Portfolio Analytics). The plan currently lists it as P7-T1, depends on P6-T1 (FYERS account sync). **Move to before P6-T1 (Portfolio Analytics start).**

**F-32. P3-T1 (FYERS bulk-quotes verification) does not block downstream P3 tasks correctly.**
The roadmap names it as a Phase 2 precondition; the plan lists it as a Phase 3 task with no explicit blocking edge to P3-T7/T8/T9 (HDS / DS / live quotes). **Add explicit `depends_on` for any task that uses FYERS quotes.**

**F-33. P2-T9 (FYERS CNC sandbox verification) only blocks P8 but should also branch P6 and produce the fallback wiring tasks.**
The roadmap (Phase 6 conditional Phase 6 build items) and the system-architecture branded-button "Precondition failure path" require, on a failed CNC verification, that the REQ-ORDER-017/017a deep-link handoff UX be wired into every action surface and that platform-native execution be deferred to REQ-NEXT-011. The plan has no fallback task. **Add a conditional task set "P8-FALLBACK-T*" with explicit branching from P2-T9 outcome.**

**F-34. ADR-0004 (corporate action detection threshold) calibration is not a prerequisite for any plan task even though the roadmap requires it before Phase 5.**
See F-31.

**F-35. Phase 1 (CNC sandbox verification, P2-T9) is correctly placed but its outcome must trigger plan changes.**
The agent runbook (`agent.md`) does not describe how to handle a branch decision. **Update `agent.md` and `conventions.md` to allow conditional task activation when an ADR result alters scope.**

**F-36. P5-T2 (EODSR) depends on P5-T1 + P4-T1 but should also depend on the Signal Subscription versioning copy-on-write task (REQ-STRAT-017b) and the single-pass read constraint (REQ-STRAT-013a).**

**F-37. P7-T5 (wire RME into backtests + live workflows) depends on P4-T3 + P5-T2 but cannot succeed unless LMDS exists. Since LMDS is missing entirely, this task as written cannot be completed.**

**F-38. P8 conditional entry is captured ("only if Phase 1 CNC verification passed") but the plan does not describe how to mark P8 tasks as inactive when the verification fails.**

**F-39. P9-T3 (corporate actions) depends on P3-T6 + P6-T2 but should also depend on the corporate-action calibration note (ADR-0004) being committed.**

**F-40. P8-T5 (callback handler + LADS reconciliation) depends on P8-T4 (intent ledger) but cannot be tested without LADS being already in Live Operations or Portfolio Analytics. Add explicit `depends_on: P6-T1` (LADS exists).**

### 2.3 Tasks too coarse for atomic agent execution

**F-41. P7-T2 ("RME core module + plugin interfaces + state machine") spans an entire engine.**
Decompose into: (a) RME module skeleton + DI scaffolding; (b) position lifecycle state machine + transition validators; (c) `_version` OCC field + filter-based writes; (d) ADR-0003 channel registry + per-position consumer task; (e) RME event types + dispatch.

**F-42. P7-T3 ("Position sizing + stop mechanisms (pluggable)") spans 4 sizing models + 7 stop types.**
Decompose into: sizing-model interface + Fixed % default; ATR sizing; Portfolio Heat sizing; Drawdown-adjusted sizing; stop interface; Fixed stop; ATR stop; Trailing % stop; Trailing ATR stop; Swing Low stop; Break-Even stop; Time Stop with calendar-aware date computation.

**F-43. P7-T4 ("Portfolio heat + drawdown tracking + advisory computations") combines three large topics.**
Decompose into: portfolio heat calculator + maximum enforcement; drawdown tracker (graduated thresholds, REQ-DRDN-003); add/reduce level computation + z-score overlay; pre-trade portfolio impact panel.

**F-44. P3-T5 ("MDP interface + FYERS adapter") combines abstraction and concrete.**
Decompose into: MDP interface contract; FYERS adapter (REST only per REQ-MARKET-002a/b); rate-limit / throttle layer (REQ-RATE-003/004); cross-provider swap test scaffolding (REQ-MARKET-002d).

**F-45. P5-T3 ("Notifications store + Telegram delivery worker with retry/backoff") combines notification persistence with delivery.**
Decompose: notification record schema + write paths from LMDS/EODSR/RME; Notification Delivery Job (sole dispatcher) with Telegram client + Retry-After honoring; portal feed read path; failure visibility on portal feed.

**F-46. P6-T1 ("Trades/holdings/positions/orders sync (FYERS via user token)") names "sync" but should explicitly say LADS, list its responsibilities (orders, trades, positions, holdings; intent reconciliation in P8), and call out the per-user lock requirement and the `lads_sustained_failure_active` graduated-suspension behaviour (REQ-PORT-021a).**

**F-47. P9-T1 ("Admin job monitoring + manual retry controls") is too broad.**
See F-27.

### 2.4 Scope and guardrail risks

**F-48. The plan does not enforce the "no domain code talks to FYERS directly" guardrail at the task level.**
P3-T9 ("Live quote flow routed through MDP + portal view") is the only mention. Add the constraint to every task that touches market data or account data so the agent cannot regress.

**F-49. The plan does not enforce "browser-tier-only" for the FYERS Data WebSocket.**
REQ-MARKET-002a/b prohibits backend WebSocket consumption of FYERS Data. F-12 adds the browser flow. Make sure no task mentions "stream FYERS websocket from worker."

**F-50. The plan does not enforce "no order placement REST in backend."**
REQ-ORDER-006 prohibits backend calls to FYERS order placement. P8 tasks must repeat the constraint as a top-line invariant.

**F-51. The plan does not call out the single-instance Worker invariant beyond P1-T4.**
REQ-RME-CONC-006 has three enforcement layers; the plan lists only the Redis lease. Add IaC + pipeline-gate verification tasks. Mark the entire scaffolding as frozen-until-Phase-C per the roadmap's "One-time scaffolding" paragraph.

**F-52. Short-side and non-Nifty-500 guardrails (REQ-BOUNDARY-001/002) are not asserted.**
Add an "active enforcement" task that ensures the Phase 1 modal, the signed payload endpoint, the universe sync, and the trade ledger ingest reject non-Nifty-500 symbols and `SELL`-as-entry attempts.

**F-53. `sys_config` connection-string / secret prohibition (REQ-CONFIG-004) is not asserted at the seeder task.**
P2-T5 must include a validator that rejects any manifest row whose key matches a secret pattern.

### 2.5 Operational realism

**F-54. The plan lacks runbook authorship tasks for the five Phase A mandatory runbooks.**
DataSync recovery, admin FYERS token re-auth, data-breach response (Phase A stubs exist; need refinement); RME concurrency-freeze resolution; MongoDB failover / LADS recovery (both missing). Add an explicit task per runbook in Phase 8 (or earlier where the underlying capability lands).

**F-55. Maintenance window display + admin job interference protection is missing.**
A-14 in the build-docs review added the maintenance-window subsection; the plan does not surface this in any admin-portal task.

**F-56. CI gates not enumerated.**
The plan should include explicit CI gates for: dep scan (REQ-SEC-007), SAST (REQ-SEC-008), secret scan (REQ-SEC-009), unit-test coverage (`70%` baseline; `95%` RME carve-out), contract tests, axe-core accessibility scan, OpenAPI/contract diff (REQ-NFR-016), nightly BTE-vs-RME parity (REQ-NFR-017).

**F-57. Audit events collection is referenced everywhere but never created.**
Add a task that lands `audit_events` schema, indexes, and the helper APIs every admin-action handler must call.

### 2.6 Plan / agent runbook process gaps

**F-58. `agent.md` does not specify behaviour when a task surfaces a missing requirement or contradiction.**
`conventions.md` § Cross-agent interoperability rules already says "If a task discovers a roadmap/requirements conflict: stop the task and record the conflict." Make this explicit in `agent.md` step 3, and add a corresponding task in the conflict-resolution flow that proposes an ADR doc update before continuing.

**F-59. `agent.md` step 4 ("Verify") does not require traceability back to REQ-IDs.**
Each task in the revised plan should list the REQ-IDs it satisfies; the verification step should assert that every named REQ-ID is observably true.

**F-60. `agent.md` step 7 (Output to user) does not require a coverage delta.**
The status report should name what fraction of the phase's REQ-IDs is now satisfied, so the user can detect drift.

**F-61. `status.json` does not record per-task REQ coverage.**
Add a `coverage` map per task ID so `status.json` is self-describing.

**F-62. The plan has no explicit "phase-gate review" task.**
Each phase should end with a phase-gate review that verifies all REQ-IDs scoped to the phase are satisfied and that any "frozen scaffolding" items have not been re-opened.

---

## 3. Proposed Amendments

The following changes are proposed against `execution_plan/execution_plan.md`, `execution_plan/milestones.md`, `execution_plan/agent.md`, and `execution_plan/conventions.md`. The clean revised versions are at `execution_plan/execution_plan_revised.md` and `execution_plan/milestones_revised.md`.

### 3.1 Add explicit REQ-ID coverage and frozen-scaffolding markers

- Each task carries `REQ:` and `Touches:` lines naming the REQ-IDs it satisfies and the components it touches.
- Each task carries a `Vertical slice:` line describing the smallest end-to-end demonstration of the change.
- Phase 0 SRE/scaffolding items are marked `Frozen-after-author` and may not be re-opened in Phase 1–7 without an explicit phase-gate decision recorded as an ADR amendment.

### 3.2 Decompose the over-coarse tasks (F-41..F-47)

- Replace P7-T2 with five sub-tasks (RME skeleton; lifecycle state machine; OCC `_version`; channel registry; event dispatch).
- Replace P7-T3 with twelve sub-tasks (sizing interface + four models; stop interface + seven types).
- Replace P7-T4 with four sub-tasks (heat; drawdown; add/reduce + z-score; pre-trade impact).
- Replace P3-T5 with four sub-tasks (interface; FYERS REST adapter; throttle layer; cross-provider swap test).
- Replace P5-T3 with four sub-tasks (notification store; NDJ; portal feed; failure visibility).
- Replace P6-T1 with three sub-tasks (LADS skeleton; per-user trade-ledger lock + fencing token + snapshot version; intent reconciliation hook).
- Replace P9-T1 with eleven sub-tasks per F-27.

### 3.3 Insert the missing components (F-1..F-30)

- LMDS as P5-T6 (after notifications store, before portal feed wiring).
- ADR-0003 channel registry + singleton enforcement layers as P0-T-RME-CONC.
- Per-user trade-ledger lock as P5-T-LEDGER-LOCK (P6-T2 prerequisite).
- Sessions collection + lifecycle as P2-T-SESSIONS (P2-T1 prerequisite).
- Legal/Privacy/DPDP intake as a new sub-phase between P2 (Identity) and P3 (Universe).
- Symbol Validity Probe + ISIN-rename detection as P3-T-PROBE.
- Universe Sync diff/preview/rollback as P3-T-UNI-DIFF.
- Trading calendar coverage check as P3-T-CAL-COVER.
- WebSocket fan-out infrastructure as P5-T-WS.
- BCP/backups + Key Vault + admin MFA as P0-T-BCP.
- Security baseline (CSRF/CSP/headers/WAF/rate limit/redaction policy) as P0-T-SEC and P2-T-SEC.
- Account recovery as P2-T-RECOVERY.
- Admin step-up re-auth as P2-T-STEPUP.
- Notification types + templates + bot provisioning broken out per F-18 / F-30.
- Frontend↔API contract test suite as P0-T-CONTRACTS (gate before P8).
- BTE-vs-RME parity check as P7-T-PARITY.
- State-transition matrix as P7-T-MATRIX (gate before P7-T2).
- Operating-phase transition + Legal Posture widget + tester ceiling enforcement as P8 sub-tasks.
- Admin "View as user" impersonation as P8 sub-task.
- Data-management.md collections provisioning as P0-T-COLLECTIONS (single Mongo.Migration migration that lands every collection schema, indexes, TTL, archive policies).
- Calibration-note prerequisite (ADR-0004) moved to P5 entry (was P7-T1).
- Pre-market admin FYERS token check as P5-T-PREMARKET-CHECK.
- WCAG/axe in CI as P0-T-ACCESS-CI; manual WCAG audit as P8 sub-task.

### 3.4 Reorder for sequencing correctness (F-31..F-40)

- Move calibration-note from P7-T1 to a new P5 entry-precondition task.
- Make P3-T1 (FYERS bulk-quotes verification) a hard dependency of P3-T7/T8/T9 (HDS, DS, live quotes).
- Add a conditional fallback task block triggered by the P2-T9 outcome.
- Add explicit `depends_on: P6-T-LADS` for P8-T5.
- Add ADR-0004 commit as a dependency on P9-T3 (corporate actions).

### 3.5 Tighten guardrails inside task descriptions (F-48..F-53)

- Every task that touches market data: explicit "must use MDP interface; no direct provider call."
- Every task that touches FYERS account data: explicit "user token only; never admin token; backend REST only."
- Every task that touches order placement: explicit "no backend REST call to FYERS order placement; widget submits."
- Every task that touches `sys_config`: explicit "no secret material; no connection strings; audit before write."
- Every Phase 0 SRE/scaffolding task: explicit "Frozen-after-author."

### 3.6 Update `agent.md` and `conventions.md` (F-58..F-62)

- Step 3: "If you discover a missing requirement or a contradiction with the canonical docs, stop and create a conflict-resolution task per `conventions.md` § cross-agent rules; do not patch the conflict in the current task."
- Step 4: "Verify each REQ-ID listed on the task is observably true."
- Step 7: "Output the REQ coverage delta (REQs satisfied this run / REQs scoped to phase)."
- `status.json`: add `phase_coverage` map (`phase_id → satisfied REQ-IDs`).
- New step 8: "If this is the last task in a phase, run the phase-gate verification routine."

### 3.7 Add explicit phase-gate verification tasks

After every phase, a phase-gate task verifies:
- All REQ-IDs scoped to the phase appear in `status.json.phase_coverage[phase_id]`.
- Every "Frozen-after-author" item has been touched only by the agent that authored it.
- The phase's exit-readiness criteria (per `milestones.md`) hold.

---

## 4. Revised Execution Plan

The full revised plan is at `execution_plan/execution_plan_revised.md`. A condensed view is below; the revised plan file carries the full per-task specification (REQ-IDs, dependencies, vertical slice, frozen-after-author markers, verification criteria).

### Phase P0 — Execution Plan System (v0.1)
- P0-T1 — Bootstrap versioned execution plan system (already complete).
- P0-T2 — Add single-line continuation runbook (already complete).
- P0-T3 — Update `agent.md` + `conventions.md` to require REQ-ID coverage, conflict-resolution branching, phase-gate verification (NEW; supersedes parts of P0-T2).

### Phase P1 — Foundation, SRE Scaffolding, Data Foundation (v0.2)
*All items in this phase are Frozen-after-author per the roadmap's "One-time scaffolding" paragraph.*
- P1-T1 — Solution / workspace scaffolding (existing P1-T1).
- P1-T2 — Local dev containers + bootstrap docs (existing P1-T2).
- P1-T3 — API baseline + versioned API root + structured logging (existing).
- P1-T4 — Worker baseline + three-layer singleton enforcement (IaC + pipeline gate + Redis lease) (existing, expanded).
- P1-T5 — OpenTelemetry + OTLP wiring + collector dual-replica + saturation policy + fallback endpoint (existing, expanded for ADR-0002 / REQ-NFR-009a).
- P1-T6 — Azure App Configuration + Key Vault bootstrap + LKG cache wiring + max-age + staleness alert (existing, expanded for ADR-0002).
- P1-T7 — Serilog destructuring redaction policy + unit test (NEW; engineering-standards § Security Standards).
- P1-T8 — Security baseline: TLS, security headers, CSP nonce, HSTS, X-Frame-Options, Permissions-Policy, WAF (REQ-SEC-001..005, REQ-NFR-013) (NEW).
- P1-T9 — CI gates: dep scan, SAST, secret scan, axe-core, contract-diff scaffolding, coverage thresholds (REQ-SEC-007/008/009, REQ-NFR-014, REQ-NFR-016, REQ-ACCESS-001) (NEW).
- P1-T10 — BCP / backup configuration: MongoDB Atlas PITR, PostgreSQL market+backtest backups, Key Vault soft-delete + purge protection + 90-day retention, geo-redundant secondary in South India, restore drill placeholder runbook (REQ-BCP-001..009) (NEW).
- P1-T11 — Mongo.Migration: provision the full `data-management.md` collection catalog with indexes, TTL/Online-Archive policies, required fields (REQ-DATA-001..006a) (NEW).
- P1-T12 — Per-position channel registry skeleton (ADR-0003): registry, channel lifecycle, per-position consumer task, EOD trailing-stop carve-out, idle-channel cap (REQ-RME-CONC-001..006) (NEW).
- P1-T13 — Per-user trade-ledger write lock primitives (REQ-PORT-031/031a/031b): Redis lock + fencing token + ledger_snapshot_version helper; not yet wired into ledger writers (NEW).
- P1-T14 — Phase-gate verification task for Phase 1 (NEW).

### Phase P2 — Identity, Sessions, Legal/Privacy, Admin Bootstrap, sys_config (v0.3)
- P2-T1 — OAuth sign-in (Google, Microsoft, Facebook/Meta) end-to-end with Bearer JWT in header (REQ-AUTH-001..002, engineering-standards § CSRF model).
- P2-T2 — `sessions` collection: 24h TTL, single-active-session, FYERS-required redirect, expiry warning, redirect-on-expiry (REQ-SESSION-001..013).
- P2-T3 — Identity model + roles + approval state + Phase A tester ceiling enforcement at approve-time (REQ-ROLE-001..005, REQ-LEGAL-003).
- P2-T4 — Lazy bootstrap admin per `SEED_ADMIN_EMAIL` + admin-OAuth restricted to Google/Microsoft + `amr` MFA-claim enforcement at admin sign-in (REQ-ROLE-005, REQ-BCP-009).
- P2-T5 — Admin step-up re-authentication (5-minute fresh-OAuth) helper + audit chain (REQ-SEC-011).
- P2-T6 — Account recovery: linked secondary OAuth identity + admin-assisted rebind with step-up (REQ-RECOVERY-001..006).
- P2-T7 — FYERS credential management + token lifecycle + Key Vault storage + dirty-token UX + admin token banner (REQ-AUTH-003..010, REQ-SESSION-009).
- P2-T8 — Latest-tab-wins PLD WebSocket session lease in Redis (REQ-SESSION-014).
- P2-T9 — `sys_config` seeder console app + manifest + sentinel + per-deployment key resolution + secret-prohibition validator (REQ-CONFIG-009..011, REQ-CONFIG-004).
- P2-T10 — Deployment workflow: FluentMigrator → Mongo.Migration → seeder → API/Worker activation, with sentinel-row startup check in API and Worker (REQ-CONFIG-010, REQ-MIGRATION-002).
- P2-T11 — Admin `sys_config` management UI: list/search/edit/reset/audit + step-up gating for `risk`/`legal`/`operations` categories + REQ-CONFIG-005a write audit chain.
- P2-T12 — Admin approval flows + non-approved user lock-out screen (REQ-ROLE-004, REQ-SESSION-012/013).
- P2-T13 — Versioned legal documents at stable URLs: ToS, Privacy Policy, long-form disclaimer, Phase A tester acknowledgement (REQ-LEGAL-007/008, REQ-LEGAL-004).
- P2-T14 — Signup intake: three distinct affirmative checkboxes (ToS, Privacy, Acknowledgement) + minor self-declaration; bumped-version re-acceptance prompt at next login (REQ-LEGAL-004/008, REQ-PRIVACY-003/007).
- P2-T15 — Privacy notice + RoPA at `docs/privacy/ropa.md` + grievance officer page (REQ-PRIVACY-002/005/010).
- P2-T16 — Operating-phase recording in `sys_config` and audit (`operations.phase.current`, REQ-LEGAL-001).
- P2-T17 — REQ-ORDER-010b CNC sandbox verification gate (existing P2-T9). Outcome recorded as ADR; activates P8 if pass, P8-FALLBACK if fail.
- P2-T18 — Phase-gate verification for Phase 2.

### Phase P3 — Universe, Calendar, Market Data Provider (v0.4)
- P3-T1 — FYERS bulk-quotes rate-limit accounting verification + ops note + sys_config default adjustment (precondition; blocks P3-T6+) (REQ-MARKET-002c, ADR-0005).
- P3-T2 — Symbol master + universe state models + `sql_table_name_suffix` deterministic generation + collision check at boot (REQ-UNIV-001..010, REQ-HIST-006/007/008/008a).
- P3-T3 — Internal trading calendar admin UI + IST canonical zone + session vs explicit-non-trading-day distinction (REQ-CALENDAR-001..006).
- P3-T4 — Calendar coverage check + admin warning + Legal Posture widget integration + Phase A→B transition gate (REQ-CALENDAR-007).
- P3-T5 — Universe Sync flow: CSV validation; pre-commit diff preview; rename detection by ISIN; archive-confirm threshold + typed phrase + justification; rollback (30-day window) (REQ-UNIV-011..020).
- P3-T6 — Universe upload page: live symbol master with health indicator (out-of-sync count); single-action reseed of out-of-sync; auto-trigger HDS for net-new symbols; in-flight progress (REQ-UNIV-014..015c).
- P3-T7 — Symbol Validity Probe: scheduling on first daily admin token; per-symbol classification (Success / Transient / Unknown); admin work queue; rename / delisting / dismiss resolution flow with in-place suffix preservation (REQ-UNIV-021..021b).
- P3-T8 — MDP interface contract + `IMarketDataProvider` operations (history, quote) + REST-only constraint codified (REQ-MARKET-002a/b, engineering-standards § MDP Standards).
- P3-T9 — FYERS adapter (REST only; rate-limit / throttle layer; per-second/minute/day budgets; back-off; daily budget metric) (REQ-RATE-003/004/011/012, integrations.fyers.* sys_config keys).
- P3-T10 — Cross-provider swap test scaffolding with a stub TrueData adapter (REQ-MARKET-002d).
- P3-T11 — PostgreSQL historical schema + symbol-table mapping service (single shared `ISymbolTableMapping`) + chart data endpoints + REST query pattern (REQ-HIST-001..011, REQ-HIST-010a).
- P3-T12 — HistoricDataSeed: scoped per-symbol table materialisation (REQ-HIST-009a) + idempotent + resumable + earliest-available-date persistence (REQ-HIST-009).
- P3-T13 — DataSync (DS) post-market job: 10-session recovery buffer; weekly/monthly upserts; success marker; admin-token freshness re-check at every batch boundary (REQ-MARKET-005/005a/006/007).
- P3-T14 — Live quote / portal live data flow using browser-tier FYERS Data WebSocket + PLD lease + MDP REST fallback (REQ-MARKET-002b, REQ-DASH-013, REQ-STOP-006c).
- P3-T15 — Phase-gate verification for Phase 3.

### Phase P4 — Strategy Research and Backtesting (v0.5)
- P4-T1 — Signal Subscription model + versioned parameter sets + copy-on-write versioning + pause/resume mid-EODSR safety (REQ-STRAT-007a/b, REQ-STRAT-017b).
- P4-T2 — Historical query layer + shared timeframe & calendar layer (single implementation across charting, backtest, EODSR) + rolling 3/5/7-day candle computation on demand (REQ-TIMEFRAME-001..006, REQ-CALENDAR-003).
- P4-T3 — Backtest engine + result storage in `BT_`-prefixed tables + slippage/commission model + R-based metrics + low-confidence flagging (REQ-STRAT-011b/023, portfolio-risk-guidelines § Backtesting Rules).
- P4-T4 — Signal Builder + Backtest Result screens; backtest starting equity = notional default per CG-9 (REQ-BTSTORE-006a).
- P4-T5 — Phase-gate verification for Phase 4.

### Phase P5 — Live Operations, Portfolio Analytics (v0.6)
*Entry precondition: ADR-0004 calibration note for corporate-action detection threshold is committed.*
- P5-T1 — DataSync EOD success marker contract + EODSR sequencing gate (REQ-MARKET-007).
- P5-T2 — EOD Signal Runner (EODSR): single-pass per-symbol read into worker memory (REQ-STRAT-013a); write entry signals to MongoDB; provenance fields per REQ-STRAT-028; atomic-run semantics (REQ-STRAT-027); db-retry max bound (`eodsr.db_retry.max_seconds`).
- P5-T3 — Notifications collection: notification record schema + write path from EODSR/LMDS/RME; required notification-type enum (REQ-NOTIFY-006).
- P5-T4 — Notification Delivery Job (NDJ): sole Telegram dispatcher; exponential back-off; honor Telegram `Retry-After`; dirty-link auto-disable at threshold; global-disable break-glass flag (REQ-NOTIFY-008/019/022a).
- P5-T5 — Telegram bot provisioning: `notification_bots` collection; deep-link subscription token; bot token rotation flow with step-up; replace-bot audit (REQ-NOTIFY-015/016/017/022a).
- P5-T6 — Portal notification feed: filter, unread count (critical + total per REQ-NOTIFY-014), deep links, failed-delivery flagging.
- P5-T7 — Live Account Data Scan (LADS) skeleton: per-user FYERS token; orders/trades/positions/holdings sync; idempotent ingestion; first-time backfill; intraday interval (`jobs.account_sync.intraday_interval_minutes`); LADS sustained-failure suspension (REQ-PORT-018..021a).
- P5-T8 — Per-user trade-ledger write lock wired into all ledger writers: LADS, on-login sync, EOD sync, manual sync, admin rebuild, single-symbol backfill, manual adjustments (REQ-PORT-031 + U-8 resolution).
- P5-T9 — Immutable trade ledger ingestion: append-only, idempotent, FIFO; non-Nifty-500 holdings silently excluded (REQ-PORT-005a).
- P5-T10 — Reconciliation (Phase A subset, REQ-RECON-001..004): auto-sync failure handling, recovery order, no-silent-edit, MongoDB adjustment-entry recording.
- P5-T11 — Holdings + PnL + reconciliation views (REQ-DASH-*).
- P5-T12 — Live Market Data Scan (LMDS): continuous market-hours job; shared admin FYERS token; bulk-quote polling at `jobs.live_market_scan.poll_interval_seconds`; per-position level evaluation; quote-delay indicator; capacity warning at `jobs.lmds.capacity_warn_pct`; calendar-aware startup; circuit-clear re-evaluation (REQ-STOP-006/006a/006b/006c, REQ-SLO-004/010/011, REQ-PLC-004).
- P5-T13 — WebSocket fan-out: portal client connections; Redis pub/sub from worker writers; stop-breach + add/reduce + pending-entry-supersede push events; degraded fallback to polling on Redis loss (REQ-NFR-013, REQ-DATA-006a).
- P5-T14 — Pre-market admin FYERS token validity check at 08:30 IST (REQ-NOTIFY-022/022b).
- P5-T15 — MDP failure-mode behaviour: shared-ingestion token loss suspends LMDS/DS/EODSR; user-token paths unaffected; admin banner + one-time Telegram alert (REQ-MARKET-016).
- P5-T16 — Honor universe archive + scan-exclusion in EODSR + LMDS; `symbol_archived_with_open_position` notification on archive (REQ-UNIV-015).
- P5-T17 — Phase-gate verification for Phase 5.

### Phase P6 — Risk Management Engine (v0.7)
- P6-T1 — Authoritative state-transition matrix (REQ-PLC-002a) committed as a documentation prerequisite for the contract-test floor.
- P6-T2 — RME module skeleton + DI scaffolding + interface contracts (sizing, stops, advisories).
- P6-T3 — Position lifecycle state machine + transition validators per the matrix (REQ-PLC-002, REQ-PLC-005a producers table).
- P6-T4 — Position document `_version` field (OCC) + filter-based writes + retry semantics + REQ-PORT-031b fencing-token integration (REQ-RME-CONC-002, REQ-PORT-031b).
- P6-T5 — RME event types + per-position channel dispatch wiring against the registry from P1-T12 (REQ-RME-CONC-001..005).
- P6-T6 — Snapshot-consistent equity-base read + override-active retry message + manual equity override with cap (REQ-RME-006a/b/c/d, REQ-PORT-005a, S-10 cap).
- P6-T7 — Equity-base divergence advisory + dashboard card + one-time Telegram (REQ-RME-006e).
- P6-T8 — Position sizing interface + Fixed % default model.
- P6-T9 — ATR-based sizing model.
- P6-T10 — Portfolio Heat-based sizing model.
- P6-T11 — Drawdown-adjusted sizing model.
- P6-T12 — Stop-loss interface + Fixed Stop.
- P6-T13 — ATR Stop.
- P6-T14 — Trailing Stop (percentage).
- P6-T15 — Trailing Stop (ATR) with EOD trailing-stop carve-out per ADR-0003.
- P6-T16 — Swing Low Stop.
- P6-T17 — Break-Even Stop (default `risk.stop.breakeven_trigger_r`).
- P6-T18 — Time Stop with calendar-aware `time_stop_date` + post-calendar-edit recompute job (REQ-STOP-003/003a).
- P6-T19 — Portfolio heat calculator + maximum enforcement + correlated-industry grouping (REQ-HEAT-002, REQ-SIZING-014a).
- P6-T20 — Drawdown tracker: graduated thresholds; advisory vs enforcement split; sys_config keys under `risk.drawdown_advisory.*` and `risk.drawdown_enforcement.*` (REQ-DRDN-002/003/004).
- P6-T21 — Pyramiding + add/reduce level computation + z-score overlay + never-lowers-stop floor (REQ-PYR-003/011, REQ-SIZING-007).
- P6-T22 — Pre-trade portfolio impact panel: projected heat, sector exposure, cash reserve, correlation context, worst-case stop (portfolio-risk-guidelines).
- P6-T23 — RME profile concept + RME profile optimisation backtests (Composite Score + Robustness Factor + multi-timeframe; share REQ-SLO-007 cap with backtests) (REQ-RME-020/020a/b/c/021).
- P6-T24 — Wire RME into backtests + live workflows: shared implementation across BTE and live (REQ-RME-003); BTE-vs-RME parity nightly check + admin alert + `operations.bte_rme_parity.enabled` toggle (REQ-NFR-017).
- P6-T25 — Admin global kill switch + per-user / per-strategy enable/disable + step-up gating; ADMIN-source position suspension reason codes (REQ-ADMIN-007/010/013, REQ-PLC-005a, REQ-ADMIN-016).
- P6-T26 — `rme_incidents` collection + admin incident dashboard + frozen-with-visibility resolution flow + runbook #4 reference (REQ-RME-CONC-007).
- P6-T27 — Portal: full RME advisory panel on chart page; positions-summary RME state; portfolio health strip; per-position channel backlog gauge; idle-channel cap warning (REQ-CHART-*; RME-L1).
- P6-T28 — Dashboard: portfolio summary + XIRR + period performance + sector breakdown + portfolio-level freshness indicator (REQ-DASH-012, T-9).
- P6-T29 — Telegram entry-signal template (brief portfolio impact summary, chart deep link) + exit-advisory template (REQ-NOTIFY-024/025).
- P6-T30 — Phase-gate verification for Phase 6.

### Phase P7 — Execution Assistance (v0.8) — conditional on REQ-ORDER-010b pass
*Entry precondition: P2-T17 outcome is "pass". Otherwise execute P7-FALLBACK.*
- P7-T1 — Embed FYERS API Connect SDK; pin URL; pin SHA-256 hash; deployment-time hash diff; trigger contract suite on change (REQ-ORDER-016a).
- P7-T2 — Frontend ↔ API contract test suite as CI gate (REQ-NFR-016) (turned on; was scaffolded in P1-T9).
- P7-T3 — Pre-flight check endpoint: token validity, kill switch, drawdown enforcement (REQ-DRDN-003 category a), duplicate-in-flight, halt state (REQ-HALT-005), LADS sustained-failure (RME-R2).
- P7-T4 — Phase 1 platform modal: Entry/Add/Reduce/Exit; CNC enforced; Market/Limit; quote auto-refresh at `orders.phase1_quote_refresh_seconds`; deviation warning re-surface (CG-7); halt warning auto-display via WebSocket (REQ-HALT-005).
- P7-T5 — Signed-payload endpoint: HMAC + nonce + session/action binding; multi-intent heat gate (EC-5); LADS health gate (RME-R2); visibility-change refresh listener (REQ-ORDER-009b/009c).
- P7-T6 — `intent_ledger` collection + indexes + nonce uniqueness + write before widget activation; chosen capture strategy (capture-phase listener or two-step confirm) (REQ-ORDER-014a/014b).
- P7-T7 — `<fyers-button>` rendering from signed payload + global `finished` callback handler (intent only — never advances position lifecycle) + audit chain (REQ-ORDER-015/015e/015f).
- P7-T8 — LADS intent-reconciliation safety net: callback-matched, callback-missed, unresolved at `orders.intent_timeout_minutes`, orphan_ack (REQ-ORDER-015c).
- P7-T9 — Awaiting-confirmation portal state + LADS-gated entry-fill / partially-filled notifications (REQ-ORDER-015e/015f).
- P7-T10 — Callback reliability OpenTelemetry metrics + admin alert at `orders.callback_failure_alert_ratio` (REQ-ORDER-015d).
- P7-T11 — Persistent "Exit position" affordance on positions summary (T-8).
- P7-T12 — Contract test suite (10 tests per roadmap Phase 7 list).
- P7-T13 — Phase-gate verification for Phase 7.

### Phase P7-FALLBACK — Execution Assistance Fallback (v0.8) — conditional on REQ-ORDER-010b fail
- P7F-T1 — Wire REQ-ORDER-017/017a deep-link handoff UX into Entry/Add/Reduce/Exit surfaces and externally-opened holdings (REQ-ORDER-019).
- P7F-T2 — Verify RME advisory + Telegram + analytics + dashboard remain unchanged in fallback mode.
- P7F-T3 — Visible product-identity note: V1 ships without platform-native execution; tracked as REQ-NEXT-011.
- P7F-T4 — Phase-gate verification for fallback.

### Phase P8 — Admin Operations, Hardening, Corporate Actions (v0.9)
- P8-T1 — `audit_events` collection + helper APIs + retention.
- P8-T2 — Admin job monitoring: `job_runs` grid; per-job retry; symbol-scoped reseed; HDS resume; EODSR retry; LMDS poll-interval adjuster; manual position suspend/release; kill switch toggle; Symbol Validity Probe banner; maintenance window display.
- P8-T3 — Operating phase transition workflow + gating conditions + SEBI classification opinion gate (REQ-LEGAL-001/005/005a).
- P8-T4 — Legal Posture widget on admin home page (REQ-LEGAL-010).
- P8-T5 — Admin "View as user" impersonation: step-up + read-only enforcement + audit + idle timeout + Privacy Policy disclosure (REQ-ADMIN-015, REQ-PRIVACY-012).
- P8-T6 — Admin user management: deactivate / reactivate; per-user signal suspension; `subscription_paused` reason; `manual_admin_suspension` flow with step-up.
- P8-T7 — Phase A mandatory runbooks authored: DataSync recovery, admin FYERS token re-auth, data-breach response (refine stubs); RME concurrency-freeze resolution; MongoDB failover / LADS recovery (NEW).
- P8-T8 — Annual restore drill schedule + first-drill execution recorded in `audit_events` (REQ-BCP-007).
- P8-T9 — WCAG 2.1 AA manual audit before Phase B (REQ-ACCESS-001).
- P8-T10 — Penetration test scheduling + remediation tracking (Phase C gate, REQ-SEC-010).
- P8-T11 — Load test at the current `operations.phase_a.tester_ceiling` value (Phase B gate, engineering-standards Load-test SLO targets).
- P8-T12 — Chaos / failure-injection exercises (Phase C gate, REQ-NFR-014).
- P8-T13 — Corporate actions + symbol continuity (depends on ADR-0004 calibration note + P5-T9 trade ledger).
- P8-T14 — Phase-gate verification for Phase 8.

### Phase P9 — V1 Hardening Pass (v1.0)
- P9-T1 — Regression coverage gate: every critical workflow has integration + E2E coverage.
- P9-T2 — RME unit-test coverage at 95% + contract-test-per-state-transition floor (REQ-NFR-014, engineering-standards § Testing Standards).
- P9-T3 — Final phase-gate verification + V1 release-readiness ADR.

---

## 5. Coverage Check (REQ areas → revised plan sections)

| Requirement Area | Revised plan sections |
|---|---|
| Product boundary (REQ-BOUNDARY-001..006) | All phases — enforced as guardrail in each task; explicit checks in P3 (universe), P5 (ledger), P7 (signed payload + Phase 1 modal) |
| Operating phase + Phase A invariants (REQ-LEGAL-001..010, REQ-LEGAL-005a) | P2-T13/T14/T16, P3-T4 (calendar coverage), P8-T3/T4 |
| DPDP / privacy (REQ-PRIVACY-001..012) | P2-T14/T15, P8-T5 (impersonation RoPA), P8-T7 (data breach runbook) |
| Roles, sessions, OAuth, FYERS auth (REQ-ROLE/SESSION/AUTH/RECOVERY) | P2-T1..T8 |
| Sys_config + bootstrap + LKG cache (REQ-CONFIG-001..011, ADR-0002) | P1-T6, P2-T9..T11 |
| BCP / backups / secret rotation / MFA (REQ-BCP-001..009) | P1-T10, P2-T4 (admin MFA), P5-T5 (Telegram bot rotation), P8-T8 |
| Security baseline (REQ-SEC-001..013) | P1-T7..T9, P2-T5 (step-up), P8-T10 (pen test) |
| Schema migration (REQ-MIGRATION-001..007) | P1-T11, P2-T10 |
| Accessibility (REQ-ACCESS-001..007) | P1-T9 (axe in CI), P8-T9 (manual audit) |
| Universe + symbol master (REQ-UNIV-001..021b) | P3-T2, P3-T5, P3-T6, P3-T7 |
| Trading calendar + timeframes (REQ-CALENDAR/TIMEFRAME) | P3-T3, P3-T4, P4-T2 |
| Historical storage + suffix (REQ-HIST-001..011) | P3-T2 (suffix), P3-T11..T12 |
| Market data provider + rate management (REQ-MARKET, REQ-RATE, REQ-MKTPROV) | P3-T1, P3-T8..T10, P3-T13, P5-T15 |
| Market halt detection (REQ-HALT-001..005) | P5-T15 (MDP failure mode), P7-T3/T4 |
| Signal Subscription + EODSR + backtests (REQ-STRAT-*, REQ-BTSTORE-*) | P4-T1..T4, P5-T2 |
| RME core + sizing + stops + heat + drawdown + lifecycle (REQ-RME-*, REQ-SIZING-*, REQ-HEAT-*, REQ-DRDN-*, REQ-PLC-*, REQ-PYR-*) | P6-T1..T22 |
| RME profile optimisation + parity (REQ-RME-020..022, REQ-NFR-017) | P6-T23..T24 |
| RME concurrency (REQ-RME-CONC-001..007) | P1-T12, P6-T4..T5, P6-T26 |
| Per-user trade-ledger lock (REQ-PORT-031/031a/031b) | P1-T13 (primitives), P5-T8 (wire-up), P6-T4 (RME integration) |
| Portfolio analytics + reconciliation (REQ-PORT-*, REQ-RECON-001..004) | P5-T7..T11 |
| Notifications (REQ-NOTIFY-*) | P5-T3..T6, P6-T29 |
| WebSocket fan-out (REQ-NFR-013) | P5-T13 |
| Portal dashboard + chart + watchlist (REQ-DASH/CHART/WATCH/PROFILE) | P3-T14 (chart data), P6-T27..T28, P5-T6 (notification feed), P7-T11 (exit affordance) |
| Execution assistance + intent ledger (REQ-ORDER-*) | P7-T1..T12 (or P7-FALLBACK) |
| Admin operations (REQ-ADMIN-*) | P2-T11/T12, P8-T2..T6 |
| Logging + observability (REQ-NFR-008/009/009a, REQ-TECH-003/004) | P1-T5, P1-T7 |
| API versioning + contracts (REQ-NFR-015/016) | P1-T3, P1-T9, P7-T2 |
| Testing depth (REQ-NFR-014, engineering-standards) | P1-T9, P9-T1/T2 |
| SLOs (REQ-SLO-001..011) | observed across P5/P6/P7 (LMDS, RME, dashboard); admin alert routing in P8-T2 |

Items intentionally not in the revised plan because the docs mark them out of scope for V1: REQ-OOS-001..011, REQ-NEXT-* (deferred), REQ-DRDN-006 implementation (Phase B per the roadmap).

---

## 6. Assumptions / Open Questions

These are points where the canonical docs left a real ambiguity that the revised plan had to resolve provisionally. Each is flagged so the author can confirm or correct.

1. **`agent.md` step-7 coverage delta format.** The revised plan adds a `phase_coverage` map to `status.json`. The exact JSON shape is left to the implementer of P0-T3.

2. **Frozen-after-author enforcement.** The roadmap names the policy but does not specify the technical enforcement (CODEOWNERS rule? CI lint? phase-gate review only?). The revised plan recommends a phase-gate-review check; a CODEOWNERS lock could be added later if drift is observed.

3. **Conditional task activation in `status.json`.** When P2-T17 (CNC sandbox verification) sets the branch, `status.json.pending_tasks` should be edited to remove the inactive branch. The revised plan recommends a small helper task at the end of P2-T17 that performs that mutation. An alternative is to keep both branches in `pending_tasks` with an `active_when` predicate that the agent evaluates on load. The revised plan picks the simpler "edit on branch" approach.

4. **Per-deployment `sys_config` keys.** Several keys in `system-config.md` are flagged "(seeded per deployment — see REQ-...)". The revised plan assumes the seeder reads pipeline parameters; the per-environment value source (Azure Pipelines variable group? GitHub secret? Key Vault reference?) is not in the docs. Confirm before P2-T9.

5. **REQ-ORDER-010b verification timing.** The roadmap says "earliest point in Phase 1 at which a FYERS API Connect sandbox account is available." The revised plan keeps it in P2 (Identity) immediately after FYERS credential management. If a sandbox account is not yet provisioned at that point, the gate slides forward but P8 / P7-FALLBACK selection is blocked until then. Confirm the intended provisioning timing.

6. **Order of P3-T7 (Symbol Validity Probe) vs P3-T8 (MDP interface).** The probe needs the FYERS adapter; the revised plan therefore lists T7 after T8/T9. The roadmap text is silent on the dependency order.

7. **WebSocket transport choice.** The revised plan assumes ASP.NET Core WebSockets fronted by Redis pub/sub for fan-out. The docs do not name a specific transport; SignalR is a reasonable alternative. Confirm.

8. **CI-side contract-test runner.** REQ-NFR-016 names a "lightweight schema-comparison approach"; the revised plan does not pick a specific tool (Pact, OpenAPI diff, TypeScript-vs-fixture assertions). Confirm before P1-T9 final wiring.

9. **Calibration note authorship (ADR-0004).** ADR-0004 documents the corporate-action detection threshold and references a calibration note that must be committed before P5 implementation. The revised plan adds a P5-entry-precondition task; the author of the calibration note (admin? trader? quant?) is not named in the docs.

10. **State-transition matrix (REQ-PLC-002a) authorship.** The revised plan makes it a P6-T1 documentation prerequisite; whether it lives in `requirements-spec.md` or in a sub-document is the author's call.

---

*End of review. The revised plan at `execution_plan/execution_plan_revised.md` and `execution_plan/milestones_revised.md` is offered as the proposed replacement once these findings are accepted. Per `conventions.md`, the existing `execution_plan.md` is left unmodified pending an explicit "adopt revised plan" decision.*
