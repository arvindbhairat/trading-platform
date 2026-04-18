# Architectural Review — Build Documentation

**Reviewer role:** Senior systems architect, technical coherence only (compliance out of scope)
**Date:** 2026-04-18
**Scope:** `CLAUDE.md`, `docs/README.md`, `docs/terminology.md`, `docs/requirements-spec.md`, `docs/product-scope.md`, `docs/system-architecture.md`, `docs/implementation-roadmap.md`, `docs/engineering-standards.md`, `docs/system-config.md`, `docs/portfolio-risk-guidelines.md`, `docs/data-management.md`, `docs/repo-structure.md`, `docs/adr/0001–0004`, `docs/operations/fyers-api-budget.md`, `docs/operations/runbooks/*`.

---

## Summary

1. The **LMDS 90-second poll interval** (`operations/fyers-api-budget.md` Inputs table) is incompatible with **REQ-SLO-010** (stop-detection-to-notification p95 ≤ 15s). The two cannot both hold.
2. ~~**PendingEntry → Open authority is split** between the FYERS `finished` callback (`roadmap` Phase 7 build item 6) and LADS reconciliation (Phase 7 build item "LADS-confirmed fill evidence, not the callback alone"). No document names the single source of truth.~~ **Resolved 2026-04-18** — on re-read the spec already names LADS as the single authority for position lifecycle (REQ-ORDER-015). New REQ-ORDER-015e / 015f added to define portal awaiting-confirmation UX and LADS-gated fill notifications; roadmap Phase 7 cross-reference tightened.
3. ~~The **Worker Service single-instance assumption** is load-bearing for RME correctness (`adr/0003` § Scale context) but is not declared as a guardrail in `CLAUDE.md`, `system-architecture.md`, or the roadmap. Default Azure App Service scale-out silently breaks the model.~~ **Resolved 2026-04-18** — verified as a real gap (not a misread). New REQ-RME-CONC-006 codifies three enforcement layers (IaC + pipeline gate + Redis singleton lease); new Deployment Topology section in `system-architecture.md`; new guardrail in `CLAUDE.md`; Phase 0 roadmap bullet added; seed keys added to `system-config.md`.
4. ~~**Per-symbol SQL Server tables** (`data-management.md` § Historical Data Layer, `sql_table_name_suffix`) combined with quarterly Nifty 500 rebalance implies runtime DDL, yet `engineering-standards.md` § Data Migrations names FluentMigrator, a startup-migration tool. No runtime table-creation path is described.~~ **Resolved 2026-04-18** — FluentMigrator framing was a misread (REQ-MIGRATION-004 already rules out per-symbol DDL at startup). Real gap was that no requirement named who creates the three tables for a newly-added symbol. New REQ-HIST-009a makes HDS the per-symbol materialisation path with idempotent `IF NOT EXISTS`, per-symbol-atomic transaction, shared DDL template, and job-log audit trail. REQ-MIGRATION-004 scope clarified; REQ-UNIV-020(b) cross-referenced.
5. **Bootstrap ordering** for `sys_config` is unspecified: `system-config.md` lists ~85 seed keys that must exist before any admin can log in, but admin seeding is itself a Phase 1 deliverable that depends on runtime reads from `sys_config`.
6. The **FYERS `CNC` precondition** (`roadmap` Phase 7 Preconditions) is stated as a build blocker without a defined contingency; the Phase 7 build is the only execution-assistance route in the roadmap.
7. **Equity-base reads during an in-flight trade-ledger write** are unprotected: `REQ-PORT-031` defines a per-user ledger write lock but `REQ-RME-006a/b/c` equity-base computations have no corresponding read barrier.
8. **Duplicate position creation for the same symbol/user/Signal Subscription** is not prevented by ADR-0003's per-position channel — the channel key is position id, not (symbol, user, signal subscription), so two nearly-simultaneous entry signals can both create Open positions.
9. The **`finished` callback ↔ LADS 15-minute reconciliation gap** leaves the portal in an undefined state for up to one LADS cycle; the frontend lock policy for that window is unspecified.
10. **Exit Priority ordering vs. lifecycle state table** conflict (`portfolio-risk-guidelines.md`): "Drawdown control exit" is priority #5 but the drawdown threshold table lists drawdown responses only as advisories, not as state-changing exits. The two cannot both be true.

---

## Critical Gaps

### 1. Stop-detection SLO is unreachable at the configured LMDS interval — **RESOLVED 2026-04-18**

**Original finding (retained for audit trail):**
- **Where:** `docs/operations/fyers-api-budget.md` Inputs table (`jobs.live_market_scan.poll_interval_seconds = 90`) vs. `docs/requirements-spec.md` REQ-SLO-010.
- **Problem:** p95 ≤ 15s from breach to notification requires polling at ≤ 15s. The budget model shows 90s is load-bearing to fit the FYERS 100,000-call/day limit without bulk. Tightening to 15s breaches the budget.

**Correction:** Re-reading REQ-SLO-010, the 15 s p95 measures from "stop breach being detected within an LMDS cycle" to notification write — i.e., intra-cycle processing time, not wall-clock from the breach itself. The poll interval is not part of the 15 s. The original finding was based on a misreading of the requirement and is withdrawn. REQ-SLO-010 is correct as written.

**Real concern carried through:** user-observable breach-to-alert latency is `poll_interval + 45 s` (REQ-SLO-010 intra-cycle + REQ-SLO-005 Telegram), which at the 90 s default sums to up to ~135 s. This is explicitly accepted: the platform is a swing/position-trading decision-support tool, not an intraday or options system; sub-minute breach detection is not a product requirement.

**Resolution (2026-04-18):**
- No change to REQ-SLO-010. No change to the 90 s LMDS default.
- New `requirements-spec.md` requirements **REQ-MARKET-002a / 002b / 002c** added: backend is REST-only against market data and account data providers; FYERS Data WebSocket is browser-tier only; FYERS is documented as a testing/evaluation-phase provider with migration to a commercial third-party provider planned.
- New **Phase 2 Preconditions** block added to `implementation-roadmap.md` and `operations/fyers-api-budget.md`: bulk Quotes rate-limit accounting and the 100,000/day daily-limit assumption must be verified against the FYERS sandbox before Phase 2 market-data code is written; LMDS default poll interval is re-evaluated against the verified outcome.
- `CLAUDE.md` Default Technical Direction now explicitly notes FYERS is temporary and backend is REST-only, so future reviewers do not re-open this question.

### 2. PendingEntry → Open has two authoritative sources — **RESOLVED 2026-04-18**

**Original finding (retained for audit trail):**
- **Where:** `docs/implementation-roadmap.md` Phase 7 build items 6 and 9; `docs/requirements-spec.md` REQ-ORDER-015 / REQ-ORDER-015c.
- **Problem:** The `finished` callback marks intent `matched` (item 6); Phase 7 simultaneously says "PendingEntry → Open is driven by LADS-confirmed fill evidence, not by the callback alone" (item 9). If the callback is authoritative for intent but not for position state, a position can be `matched` at the intent layer and still `PendingEntry` at the lifecycle layer.
- **Why it matters:** The RME consumes position state, not intent state. Advisories will compute against `PendingEntry` for up to one LADS cycle after the order is actually filled.
- **Smallest fix:** Add a requirement that the callback updates the intent ledger only (never the position document); LADS is the sole authority for PendingEntry → Open. Delete or soften the implication that `match_source = "callback"` is terminal.

**Correction:** On re-reading the spec, REQ-ORDER-015 is already explicit: *"The callback confirms submission, not fill: the `PendingEntry → Open` lifecycle transition per REQ-PLC-002 must be driven by confirmed fill evidence from LADS (REQ-PORT family), not by the callback alone."* REQ-ORDER-015a corroborates for partial fills; REQ-ORDER-015c names LADS as the driver of the downstream RME lifecycle update. A single source of truth already exists — the callback writes only to `intent_ledger`, and LADS is the sole authority for `PendingEntry → Open`. The original review framing ("two authoritative sources") was a misread of the roadmap in isolation from the spec.

**Real concerns that survived the re-read:**
1. **Roadmap Phase 7 language was easy to misread.** Build items 6 and 9 sat ~80 lines apart with no cross-reference; a reader scanning the roadmap in sequence could plausibly conclude the callback was terminal for position state.
2. **Portal UX during the callback-success → LADS-confirmation window was undefined.** Worst case ~15 minutes (`jobs.account_sync.intraday_interval_minutes` default 15). During that window: intent is `matched`, position is still `PendingEntry`. REQ-ORDER-005 blocks duplicate in-flight submission for the same symbol/action, but the spec said nothing about what the chart page, positions summary, or Phase 1 modal actually show the user.
3. **Entry-fill Telegram notification gating was unstated.** Whether the "entry filled" Telegram fires on callback success (fast, risk of false positive) or on LADS-confirmed fill (slow, accurate, consistent with the fill-evidence-only discipline) was not named explicitly in REQ-NOTIFY or REQ-ORDER.

**Resolution (2026-04-18):**
- No change to REQ-ORDER-015 / 015a / 015c. They are correct as written.
- New `requirements-spec.md` requirement **REQ-ORDER-015e** added: explicit portal awaiting-confirmation state visible on chart page, positions summary, and the Phase 1 modal block; displays submission timestamp and most recent LADS poll timestamp so the worst-case wait is bounded and visible; uses "submitted to FYERS — awaiting fill confirmation from the next Live Account Data Scan" wording for the REQ-ORDER-005 block; clears on LADS-driven `PendingEntry → Open`, on partial-fill record, or on REQ-ORDER-015c `unresolved`/`orphan_ack` transition.
- New `requirements-spec.md` requirement **REQ-ORDER-015f** added: "entry filled" and "entry partially filled" user notifications (Telegram and portal feed) are LADS-gated, never fired on the callback alone. Callback success produces a portal-only, feed-free confirmation banner. Fill-independent notifications (`unresolved`, `orphan_ack`, widget-timeout error, audit log) are unchanged.
- `implementation-roadmap.md` Phase 7 build item 6 reworded with a **bold cross-reference** making the submission-vs-fill distinction impossible to miss at the roadmap layer. New build items added for REQ-ORDER-015e (awaiting-confirmation UX) and REQ-ORDER-015f (LADS-gated fill notifications).
- Phase 7 contract test suite extended with three new cases (items 8, 9, 10): callback success does not advance position lifecycle; callback success produces no Telegram or feed entry; awaiting-fill modal block wording is enforced.
- **Accelerated per-user LADS trigger on callback success was considered and rejected.** Consistent with the CG #1 decision that the platform is a swing/position-trading decision-support tool and sub-minute responsiveness is not a product requirement, the default 15-minute LADS cadence is retained. The 15-minute worst-case wait is now explicit and honest via REQ-ORDER-015e rather than papered over with an ad-hoc poll.

### 3. Worker Service single-instance constraint is not declared as a guardrail — **RESOLVED 2026-04-18**

**Original finding (retained for audit trail):**
- **Where:** `docs/adr/0003-rme-per-position-event-serialisation.md` § Scale context and § Decision vs. `CLAUDE.md` Non-Negotiable Guardrails, `docs/system-architecture.md`, `docs/implementation-roadmap.md` Phase 0.
- **Problem:** The RME correctness proof in ADR-0003 assumes exactly one Worker instance. An operator who enables Azure App Service auto-scale for the worker will silently split the channel registry and cause concurrent writes on the same position.
- **Why it matters:** This is the single most severe latent bug in the current design: it cannot be detected by unit tests, only by production race damage.
- **Smallest fix:** Add a guardrail to `CLAUDE.md` and a deployment constraint in `system-architecture.md`: "Worker Service must deploy as exactly one instance until Phase C partitioning is delivered. Scale-out is gated on a new ADR."

**Verification:** Unlike CG #1 and CG #2, this finding was not a misread. The ADR-0003 single-instance constraint was stated clearly inside the ADR itself (line 37, line 106, lines 219-228) but a grep for `single instance`, `single-instance`, `one instance`, `exactly one`, `scale-out`, `autoscale`, and `instance count` across `CLAUDE.md`, `system-architecture.md`, `requirements-spec.md`, `implementation-roadmap.md`, and `repo-structure.md` returned zero matches. The invariant lived only inside the ADR. `system-architecture.md` had no Deployment Topology / Hosting / Scale section at all, so an operator reading the architecture doc would never encounter the constraint.

**Resolution (2026-04-18) — multi-layer defence in depth:**

- **`CLAUDE.md` Non-Negotiable Guardrails** — new guardrail added: never deploy the Worker Service as more than one running instance until the Phase C partitioning extension in ADR-0003 is delivered and accepted in a new ADR; scale-out silently breaks the per-position channel invariant. API service is explicitly excluded (may scale horizontally).
- **`docs/system-architecture.md`** — new **Deployment Topology** section added (inserted between Core Architectural Boundaries and Main Components — the doc had no hosting/scale section previously). Names the single-instance Worker invariant, describes the three enforcement layers (IaC, pipeline gate, runtime self-election), describes the Redis lease behaviour and the graceful-degrade-on-Redis-outage semantics, points to ADR-0003's Phase C exit path.
- **`docs/requirements-spec.md`** — new **REQ-RME-CONC-006** added at the end of the RME Concurrency and Serialisation block. Codifies the three enforcement layers with citable detail: (a) IaC template sets `workerCount = 1` with no auto-scale rule, deployment preflight check fails the release on mismatch; (b) Azure DevOps pipeline gate asserts absence of auto-scale configuration; (c) runtime Redis singleton lease under `rme:worker:singleton` with TTL from `sys_config.operations.worker.singleton_lease_ttl_seconds` (default 60 s), refresh every 20 s, newcomer on conflict logs `singleton_violation`, writes `position_concurrency_alert`, exits non-zero; Redis unavailable at startup is a warning, not a startup block.
- **`docs/implementation-roadmap.md` Phase 0** — new bullet added naming the three enforcement layers so they are built in from day one rather than retrofitted.
- **`docs/system-config.md` Required Seed Table** — added `operations.worker.singleton_lease_ttl_seconds` (default 60, REQ-RME-CONC-006). Also added the missing `rme.channel.backlog_warn_depth` (default 20, REQ-RME-CONC-004) which was referenced by the spec but absent from the seed table.

**Design rationales:**
- **Redis lease over MongoDB lease:** Redis is already in the stack with idiomatic SETNX+EX semantics for distributed locks; the ADR already names Redis sorted sets for the Phase C partitioning extension, so the runtime self-election layer foreshadows the Phase C design rather than introducing a different primitive.
- **Hard exit over passive read-only mode:** App Service will restart a crashed process, producing a visible crash-loop signal that operators will notice. A passive/observation mode hides the misconfiguration.
- **Worker-only scope:** the API service is stateless behind MongoDB/SQL Server/Redis and is genuinely horizontally scalable. Applying the singleton constraint to the API would create a real availability problem without any correctness benefit.
- **Redis outage at Worker startup does not block startup:** consistent with ADR-0003's explicit stance that Redis is not a correctness dependency at runtime; the lease is a visibility layer, not a correctness layer. A missing lease is logged and surfaced via the admin System Health widget (REQ-ADMIN-014) so the gap is observable.

### 4. No runtime path for creating per-symbol SQL Server tables — **RESOLVED 2026-04-18**

**Original finding (retained for audit trail):**
- **Where:** `docs/data-management.md` § Historical Data Layer (per-symbol `sql_table_name_suffix`); `docs/engineering-standards.md` § Data Migrations (FluentMigrator).
- **Problem:** Quarterly Nifty 500 reconstitution adds symbols. Each new symbol needs a new SQL Server table. FluentMigrator is a build/startup tool, not a runtime DDL tool. The roadmap does not name a runtime DDL workflow.
- **Why it matters:** HistoricDataSeed for a new symbol will fail on first write with a "missing table" error unless a human runs a migration by hand.
- **Smallest fix:** Either (a) move to a single partitioned table (partition by symbol), or (b) add a declarative admin workflow: "add-symbol" triggers a CREATE TABLE via a managed helper that wraps FluentMigrator's runner programmatically, with transaction + audit.

**Correction:** Partially a misread, partially a real gap. REQ-MIGRATION-004 already explicitly rules out per-symbol DDL at application startup and requires a batched admin-triggered background migration job; REQ-MIGRATION-005 covers dual-read windows for schema evolution; REQ-UNIV-014 auto-triggers HDS for net-new symbols on CSV upload commit. The FluentMigrator-at-startup framing in the original finding was wrong — the spec already rules out that trap.

**Real gap that survived the re-read:** No requirement named *who creates the three `D_`, `W_`, `M_` tables for a newly-added symbol, or when.* REQ-HIST-009 required HDS to *write to* all three tables but assumed they already existed. REQ-UNIV-014 auto-triggered HDS for new symbols but described only data fetch, not table creation. REQ-UNIV-020(b) (rename-reject) mentioned "fresh tables" but named no mechanism. REQ-MIGRATION-004 governs *fleet-wide* schema evolution — adding a column to every per-symbol table — and is the wrong mechanism for per-symbol instantiation at universe add time. The two concerns are disjoint; only one was specified.

**Resolution (2026-04-18) — Option A (scoped runtime DDL inside HDS):**

- **`docs/requirements-spec.md`** — new **REQ-HIST-009a** added immediately after REQ-HIST-009. Names HDS responsible for idempotent creation of the three per-symbol tables before writing candles whenever a symbol has just joined the master. Four binding constraints: (a) `IF NOT EXISTS` idempotency so retry is safe; (b) per-symbol-atomic transaction so a mid-creation failure cannot leave a symbol with one or two of the three tables present; (c) single shared DDL schema template (FluentMigrator migration rendering DDL at runtime, or a parameterised DDL helper) used by both REQ-HIST-009a for new symbols and REQ-MIGRATION-004 for fleet-wide evolution, so the schemas cannot drift; (d) every table-creation event logged to `job_runs`.
- **`docs/requirements-spec.md`** — **REQ-MIGRATION-004** extended with a scope sentence clarifying it governs fleet-wide schema evolution across already-existing per-symbol tables and does not conflict with REQ-HIST-009a.
- **`docs/requirements-spec.md`** — **REQ-UNIV-020(b)** (rename-reject resolution) amended to cite REQ-HIST-009a explicitly as the path by which fresh tables are materialised.
- **`docs/data-management.md`** — new **Per-symbol table lifecycle** paragraph added to § Market Data Database, naming HDS as the creation path for new symbols and REQ-MIGRATION-004 as the evolution path for existing fleets, and stating that both paths share a single DDL template.
- **`docs/implementation-roadmap.md`** — Phase 2 HDS build item extended with the per-symbol table-materialisation responsibility per REQ-HIST-009a.

**Options considered and rejected:**
- **Option B (single partitioned table by symbol):** Sound long-term architecture but would require rewriting `ISymbolTableMapping`, DataSync, HistoricDataSeed, every backtest query, the charting data layer, and REQ-HIST-001 through REQ-HIST-011. Migration cost is high and there is no product-level pain driving it yet. If the three-tables-per-symbol model is revisited in future, it belongs in a standalone ADR rather than a CG resolution.
- **Option C (wrap FluentMigrator runner programmatically per new symbol):** Inverts FluentMigrator's purpose. FluentMigrator is designed for versioned migrations with rollback support, not repetitive per-symbol idempotent table creation. The "migration version" question becomes incoherent (what version does symbol N's tables have?). Rejected in favour of a parameterised DDL helper that still shares its schema template with FluentMigrator's fleet-wide migrations per REQ-HIST-009a(c).

### 5. `sys_config` seed/admin bootstrap is circular
- **Where:** `docs/system-config.md` § Seed Values; `docs/implementation-roadmap.md` Phase 1 (`define and seed the sys_config collection schema and initial runtime keys` + `implement seeded bootstrap admin behavior`).
- **Problem:** The admin is a user in MongoDB; admin approval flows read from `sys_config`; `sys_config` is admin-managed. If the Worker starts against an empty database, there is no admin to seed runtime keys and no keys for the admin to approve against.
- **Why it matters:** First deploy cannot start.
- **Smallest fix:** Declare the boot sequence explicitly: (1) Worker idempotently seeds `sys_config` defaults from an embedded manifest on startup if collection is empty, (2) API seeds the bootstrap admin from env var `SEED_ADMIN_EMAIL`, (3) admin then overrides any keys.

### 6. FYERS `CNC` precondition has no fallback
- **Where:** `docs/implementation-roadmap.md` Phase 7 Preconditions, bullet 1.
- **Problem:** "If FYERS rejects `CNC` on the branded button, raise this as a blocker before any Phase 7 code is written." No Plan B exists in the docs; Phase 7 is the only execution-assistance path.
- **Why it matters:** A single sandbox test result could invalidate the entire execution tier of the product.
- **Smallest fix:** Define the contingency explicitly: revert to Phase 6 advisory-only delivery, ship execution assistance "deferred to Phase 10", explicitly document the product-scope consequence.

### 7. Equity-base read has no lock barrier during ledger writes
- **Where:** `docs/requirements-spec.md` REQ-PORT-031 (write lock) vs. REQ-RME-006a/b/c (equity base).
- **Problem:** The per-user trade-ledger Redis write lock blocks concurrent writers but not readers. RME heat and drawdown decisions read the ledger during LMDS event processing; they can read mid-write state.
- **Why it matters:** Drawdown-adjusted sizing may fire against a ledger that is missing a just-reconciled fill, producing an under- or over-sized advisory.
- **Smallest fix:** Require RME equity-base reads to use a ledger snapshot advanced atomically after each ledger write (e.g., a monotonic `ledger_snapshot_version` on the user document; RME reads at (user, snapshot_version) or explicitly waits if snapshot lags fill time).

---

## Logical Inconsistencies

### 1. Exit Priority ordering contradicts drawdown threshold semantics
- **Where:** `docs/portfolio-risk-guidelines.md` § Exit Priority (list) vs. § Drawdown Response Thresholds (table).
- **Problem:** "Drawdown control exit" is ranked #5 in Exit Priority, implying it terminates the position; the drawdown table lists every threshold as advisory, not an exit, up to 30% where the response is "stop all trading" (still worded as "surface advisory").
- **Fix:** Either remove "Drawdown control exit" from Exit Priority, or mark the ≥ 20% row in the threshold table as "block new entries and surface exit advisory" to align the two.

### 2. CNC enforcement vs. user "Market/Limit" choice framing
- **Where:** `docs/implementation-roadmap.md` Phase 7 build item 3 ("Market/Limit order type selection... CNC product type enforced") vs. `docs/product-scope.md` "user-initiated execution assistance only".
- **Problem:** Forcing CNC prevents intraday squaring-off while still offering Market/Limit. For a "decision-support" platform, the user may rationally want to square off same-day through FYERS directly — but the modal presents the choice incompletely.
- **Fix:** Either allow the user to select product type (breaking REQ-ORDER-010's CNC enforcement), or add a static warning in the Phase 1 modal describing the swing/position constraint.

### 3. `rme_events` as "audit log" vs. ADR-0003 using it for admin-review flags
- **Where:** `docs/adr/0003` § Decision ("position is flagged for admin review in `rme_events` with reason `concurrency_conflict_unresolved`") vs. `docs/data-management.md` description of `rme_events` as an immutable audit log.
- **Problem:** Using the same collection for both time-series audit records and error/flag records muddles retention and query semantics (the audit log has long retention; ops alarms typically don't).
- **Fix:** Route `concurrency_conflict_unresolved` records to `audit_events` (which already exists) or a dedicated `rme_incidents` collection and keep `rme_events` strictly event-sourced.

### 4. 80% FYERS budget threshold's projection window is undefined
- **Where:** `docs/operations/fyers-api-budget.md` § Sensitivity + REQ-RATE-012.
- **Problem:** REQ-RATE-012 blocks `sys_config` saves that would breach the 80% threshold, but the document does not say whether projection is "full day going forward at the new settings" or "already-consumed + projected remainder". An admin save mid-session has different math under the two interpretations.
- **Fix:** Specify that REQ-RATE-012 projects over a rolling 24-hour window, measured from the save moment, using the new settings for all 24 hours.

### 5. Channel lifecycle ambiguity around PendingEntry → Rejected
- **Where:** `docs/adr/0003` REQ-RME-CONC-005(a) and (b).
- **Problem:** Channels are created on PendingEntry write and destroyed on Rejected. RME pre-trade validation may move PendingEntry → Rejected synchronously inside the same request, so a channel is created and destroyed before any event is enqueued.
- **Fix:** Create the channel on first event enqueue (lazy), not on position document creation.

---

## Underspecified Areas

### 1. Duplicate position prevention at (symbol, user, subscription) level
- **Where:** `docs/adr/0003` per-position channel; `docs/requirements-spec.md` REQ-SIG / REQ-PLC.
- **Problem:** The channel serialises events on an existing position, not position creation. Two simultaneous entry signals from the same user/subscription can produce two PendingEntry documents, both valid.
- **Fix:** Require a uniqueness check (symbol + user + signal_subscription + decision_window) at position create time; a second concurrent creation must be rejected.

### 2. Post-callback, pre-LADS-reconciliation portal state — **RESOLVED 2026-04-18**
- **Where:** `docs/implementation-roadmap.md` Phase 7 build items 6 and 9.
- **Problem (original):** After a `finished` callback with `status = success`, the user's portal sees `matched` intent but `PendingEntry` position. How long does the modal show "pending"? Can the user submit another order for the same symbol? Undefined.
- **Fix (original):** Define a frontend lock keyed on `intent_ledger.id` that blocks the Phase 1 modal for that symbol until LADS confirms; add a visible "awaiting broker confirmation" UX.
- **Resolution (2026-04-18):** Addressed as part of CG #2 resolution. REQ-ORDER-015e defines the awaiting-confirmation state on chart page, positions summary, and the Phase 1 modal block (reuses the existing REQ-ORDER-005 duplicate-in-flight guard with specialised wording). REQ-ORDER-015f defines the Telegram/feed gating. Roadmap Phase 7 build items and contract tests 8–10 cover the implementation.

### 3. Trading-calendar-change impact on existing `time_stop_date`
- **Where:** `docs/requirements-spec.md` REQ-STOP-003; `docs/portfolio-risk-guidelines.md` § Stop Loss Types (Time Stop).
- **Problem:** Admin edits the internal trading calendar; open positions carry `time_stop_date` computed from the old calendar. No rule describes whether they are recomputed.
- **Fix:** Require recomputation of every non-terminal position's `time_stop_date` on every calendar edit and emit an advisory note in the position record.

### 4. OAuth provider outage UX
- **Where:** `docs/requirements-spec.md` REQ-AUTH section.
- **Problem:** Three OAuth providers are supported. If one is down, fallback UX (offer another provider, deny sign-in, queue the attempt) is undefined.
- **Fix:** Specify: login page offers all three providers, any one success is sufficient; no fallback between providers for an existing user — their bound provider is fixed.

### 5. Azure App Configuration startup-failure behaviour
- **Where:** `docs/adr/0002` § Decision (bootstrap config from App Config/env/KV) vs. `CLAUDE.md` guardrail "startup-critical configuration must not depend only on MongoDB".
- **Problem:** If App Config is unreachable, does the API refuse to start, start with last-known-good cache, or start with embedded defaults?
- **Fix:** Specify hard-fail if no cache, warm-start from cache with explicit log, and an OTEL metric for config-source-last-reached.

### 6. Survivorship bias discount mechanism
- **Where:** `docs/requirements-spec.md` REQ-STRAT-011b.
- **Problem:** The requirement names a survivorship discount but not how it is applied (multiplicative on returns, additive annotation, filter on trade count).
- **Fix:** Declare: discount is a flagged annotation on the backtest summary, not applied to numbers, and surfaced only in the result screen.

### 7. portfolio_snapshots cache staleness
- **Where:** `docs/requirements-spec.md` REQ-DASH-015/016; `docs/data-management.md` § portfolio_snapshots.
- **Problem:** Snapshot cache TTL and invalidation trigger are not declared. A confirmed FYERS fill should invalidate it, but the requirement doesn't say so.
- **Fix:** Invalidate on (a) LADS cycle that writes a new trade, (b) manual adjustment, (c) EOD close of each session.

### 8. Telegram delivery idempotency key
- **Where:** `docs/requirements-spec.md` § Notifications; `docs/data-management.md` § notifications.
- **Problem:** "Retry with back-off" is specified but no idempotency key. A crash after HTTP send but before MongoDB acknowledgement can double-deliver.
- **Fix:** Require the Notification Delivery Job to set a deterministic `client_message_id` on each Telegram call (e.g., derived from `notifications._id`) and persist the returned `telegram_message_id` before ack.

### 9. Global kill-switch scope
- **Where:** `docs/portfolio-risk-guidelines.md` § Operational Risk Controls; `docs/requirements-spec.md` REQ-ADMIN section.
- **Problem:** "Suspend all strategy signal generation platform-wide" — unclear whether LMDS stop monitoring continues, whether LADS continues, whether user charts continue to render live data.
- **Fix:** Specify: kill switch halts EODSR entry-signal emission only; LMDS stop monitoring and LADS continue; PLD charts continue.

### 10. Multi-tab session behaviour
- **Where:** `docs/system-architecture.md` § Session/FYERS Lock.
- **Problem:** Two tabs on the same user — does the FYERS session lock allow both to stream PLD, or does the second claim evict the first?
- **Fix:** Define a per-user-token session registry in Redis with "latest tab wins, previous tab receives a WebSocket close and an in-portal banner".

### 11. ADR-0004 calibration ownership and due date
- **Where:** `docs/adr/0004-corporate-action-detection-threshold.md` § Decision.
- **Problem:** Calibration gated on Phase 5, but no owner, sample source, or deliverable date.
- **Fix:** Name the owner, the NSE sample window, and a deliverable date expressed as a Phase 5 entry precondition in the roadmap.

---

## Edge Cases and Failure Modes Not Addressed

### 1. Admin FYERS token refresh fails mid-session
- **Where not addressed:** No binding in `requirements-spec.md` between admin token state and the subsystems that use it.
- **Problem:** If the daily token expires mid-day (or refresh fails at market open), LMDS and DataSync go dark. LADS (per-user tokens) continues. The platform runs in an asymmetric degraded state with no documented indicator.
- **Fix:** Emit a "shared-ingestion suspended" banner and suppress entry-signal evaluation in EODSR until the next successful token.

### 2. Time stop landing on a now-non-trading day
- **Problem:** Calendar is edited; a position's `time_stop_date` lands on what is now a holiday.
- **Fix:** Rule already implied by § 3 of Underspecified Areas (recomputation); the specific outcome should be "roll forward to the next trading session".

### 3. Split/bonus ex-date between entry and present
- **Problem:** Open position spans an ex-date. `adr/0004` describes detection; no document states the position's stop/add/reduce levels are recomputed against the adjusted series.
- **Fix:** When detection flags a corporate action on an open position, suspend RME monitoring and surface an admin-actionable advisory until the position's levels are re-anchored.

### 4. Redis key eviction of a held trade-ledger lock
- **Where:** `docs/requirements-spec.md` REQ-PORT-031, REQ-DATA-006a.
- **Problem:** Locks evicted under memory pressure can cause a second writer to succeed while the first is still executing.
- **Fix:** Use Redlock semantics with per-lock TTL and a fencing token included in the MongoDB update filter; a write with a stale token fails.

### 5. Two simultaneous entry signals for the same symbol/subscription/user
- **Already called out in Underspecified Areas § 1.**

### 6. User revokes OAuth consent with the provider
- **Problem:** No session invalidation path described beyond explicit sign-out.
- **Fix:** On OAuth token refresh failure, end the session and mark the user `require_reauth`; propagate to active WebSocket connections.

### 7. SQL Server transient outage during EODSR
- **Problem:** EODSR reads SQL Server; a transient read error during the EOD window has no defined behaviour.
- **Fix:** Specify: retry with exponential backoff for up to N seconds (config); if still failing, mark the run `failed_for_review` and emit an admin alert — do not partial-execute.

### 8. Notification Delivery Job processing queued notifications after kill switch
- **Problem:** Kill switch halts emission; in-flight but unsent notifications from earlier are unaffected.
- **Fix:** Specify: Notification Delivery Job continues delivering pre-existing queued notifications unless the admin separately drains the queue.

### 9. Widget double-click / nonce reuse
- **Where:** Phase 7 contract test 7 covers nonce replay at server, but not user UX.
- **Problem:** User clicks the FYERS widget twice; first consumes the nonce; second is silently refused by FYERS or the signed-payload server.
- **Fix:** The Phase 1 modal must disable the action button on first widget invocation and only re-enable after `finished` callback or explicit cancel.

### 10. MongoDB replica-set failover during LADS
- **Problem:** A 10–30s failover window causes the LADS cycle to abort. Next cycle runs in 15 min.
- **Fix:** Log the cycle miss as a counter metric; if two consecutive cycles miss, raise an admin alert.

---

## Questions for the Author

1. Is the Worker Service ever intended to run multi-instance before the Phase C partitioning ADR lands? If not, where is the single-instance invariant enforced mechanically (App Service plan, deployment template, CI gate)?
2. What is the authoritative signal for PendingEntry → Open: the FYERS `finished` callback, the LADS observation, or the first of either? If LADS, what is the RME's behaviour during the up-to-15-minute gap after a successful callback?
3. How is the LMDS poll interval (default 90s) reconciled with REQ-SLO-010's 15s p95 stop-detection SLO?
4. How is a SQL Server table created for a newly-added Nifty 500 constituent at runtime, and who owns that workflow?
5. What is the exact boot sequence for a fresh database (order of `sys_config` seed, admin seed, calendar seed, first login)?
6. If the admin FYERS token refresh fails at market open, which subsystems halt and which continue?
7. Is `CNC`-only a hard product requirement, or is same-day exit support a Phase 10 consideration?
8. Does the global kill switch halt LMDS stop-monitoring, or only EODSR signal emission?
9. What idempotency key does the Notification Delivery Job use to prevent double-delivery across retries?
10. Is Azure App Configuration a hard startup dependency, or does the API warm-start from a cached last-known-good?
11. When the trading calendar changes, are existing positions' `time_stop_date` values recomputed?
12. Who owns the ADR-0004 corporate-action threshold calibration, against what sample, by what date?
13. What is the multi-tab policy — can the same user hold two active FYERS PLD streams, or does latest-tab win?
14. When RME concurrency retries exhaust (3 attempts), does the position continue to process subsequent events or is it frozen until admin action?
15. Is the 80% FYERS daily-budget projection a rolling 24-hour window, a calendar-day, or something else?
