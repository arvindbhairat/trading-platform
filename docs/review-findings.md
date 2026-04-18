# Documentation Review — SignalStack Build Docs

Reviewer role: senior systems architect.
Scope: technical coherence and buildability only. Regulatory/compliance topics excluded per `docs/review-prompt.md`.
Source corpus: `CLAUDE.md`, `docs/README.md`, `docs/terminology.md`, `docs/requirements-spec.md`, `docs/product-scope.md`, `docs/system-architecture.md`, `docs/implementation-roadmap.md`, `docs/engineering-standards.md`, `docs/system-config.md`, `docs/portfolio-risk-guidelines.md`, `docs/data-management.md`, `docs/repo-structure.md`, ADRs 0001–0004.

---

## Summary

1. The FYERS API Connect order-return contract is specified two different ways in two canonical documents. The requirements spec and the roadmap are built around a `finished` callback fired by the widget; the system architecture explicitly forbids wiring any state transition to a widget-sourced callback and substitutes an `intent_ledger` + LADS reconciliation model. One of these must be retracted — they cannot both be built.
2. A position lifecycle state named `ExitPending` (and, in places, `Submitted`/`Filled`) appears in `docs/system-architecture.md` and `docs/adr/0003-rme-per-position-event-serialisation.md` but is not among the five states enumerated in `REQ-PLC-002`. Either the requirement is incomplete or the architecture is using ghost states.
3. `docs/system-config.md` violates `REQ-CONFIG-009` (which says seed entries must exist when keys are introduced): at least ten `sys_config` keys are referenced in other docs but absent from the seed table, including the `orders.intent_timeout_minutes` that the architecture reconciliation path depends on and the `rme.channel.backlog_warn_depth` mandated by `REQ-RME-CONC-004`.
4. The RME concurrency design (ADR-0003) assumes a single Worker Service instance but provides no runtime assertion; if the service is ever deployed with two instances (intentional mis-scale, restart overlap, blue/green) the in-process channel serialisation breaks silently with no detection path.
5. ADR-0003's crash-recovery story assumes "LMDS and LADS will re-detect dropped events on the next poll cycle," but only price-breach and fill events are re-detected by those pollers. Admin-originated events (kill switch, user suspension) and EOD stop-update events enqueued just before a crash are lost permanently with no replay mechanism specified.
6. Timeframe coherence across charting, backtesting, and EODSR is asserted but not architecturally enforced: there is no single library/interface named as the sole source of candle construction, session boundary, and holiday handling. Divergence is a guardrail violation waiting to happen.
7. Corporate-action detection (`ADR-0004`) is still in status `Proposed` with a default threshold the ADR itself labels unreliable; yet Phase 5 (portfolio) depends on it and the ADR is not gated in the roadmap.
8. The three-tier config/secrets split (Azure App Configuration vs Key Vault vs MongoDB `sys_config`) is described, but the doc never names which tier each concrete setting lives in. Several security-sensitive items (FYERS app secret for admin token refresh, Telegram bot token) are described as Key Vault references, but the startup behaviour if Key Vault or App Configuration is unreachable is undefined — violating the guardrail "Never make startup-critical configuration depend only on MongoDB" in the opposite direction (nothing is said about App Configuration failure either).
9. Backtest/live Signal parity is stated as a principle but the survivorship-bias treatment (`REQ-STRAT-011b`, flat 15% expectancy discount) is cosmetic and does not reconstruct the historical universe. Backtest results will systematically diverge from live Signal behaviour for any strategy whose profitability hinges on symbols that left Nifty 500.
10. Redis is treated as a non-correctness dependency for the RME (per ADR-0003) but as a correctness dependency for `trade_ledger` writes (`REQ-PORT-031` requires a Redis lock). A single Redis outage therefore has inconsistent consequences across subsystems, and no doc reconciles this.

---

## Critical gaps

### CG-1. FYERS API Connect return-path contract contradicts itself

- **Location:** `docs/requirements-spec.md` REQ-ORDER-015 / REQ-ORDER-017a and `docs/implementation-roadmap.md` Phase 7 ("handle the API Connect `finished` callback: record order ID on success") vs `docs/system-architecture.md` section "FYERS API Connect Branded Button Contract" ("The widget does not call back into the host page. The platform must not wire any state transition or user-facing outcome to a widget-sourced callback").
- **What is wrong:** One document mandates handling a widget `finished` callback; another explicitly forbids trusting a widget-sourced callback and substitutes an `intent_ledger` polled against FYERS account sync (LADS). They describe two incompatible implementations of the same flow.
- **Why it matters:** This is the platform's sole order-placement path. Building to the requirements spec produces a system whose order confirmation, idempotency, and timeout behaviour all diverge from the architecture's reconciliation model. The mismatch also determines whether `intent_ledger` is a required collection at all — it is in the architecture but absent from `docs/data-management.md`.
- **Concrete fix:** Pick one model and retract the other. If the FYERS widget genuinely does not call back, delete REQ-ORDER-015 and rewrite REQ-ORDER-017a around a reconciliation window; add `intent_ledger` to `docs/data-management.md`; renumber `orders.intent_timeout_minutes` into the `sys_config` seed table. If the widget does call back, delete the "widget does not call back" sentence in architecture, remove the intent-ledger reconciliation path, and specify callback security (origin check, signature/nonce).

### CG-2. Phantom position lifecycle states in architecture and ADR-0003

- **Location:** `docs/system-architecture.md` "RME Signal and Advisory Flow" step referencing `ExitPending`; ADR-0003 Context table ("Open → ExitPending via Suspended"); also references to `Submitted` and `Filled` lifecycle hops. vs `REQ-PLC-002` which enumerates exactly `PendingEntry`, `Open`, `Suspended`, `Closed`, `Rejected`, and `portfolio-risk-guidelines.md` section "Position Lifecycle States" which is consistent with REQ-PLC-002.
- **What is wrong:** Downstream docs use states that are not in the canonical list. A developer implementing against architecture will encode an `ExitPending` state; a developer implementing against requirements will not; schema and RME state-machine unit tests will disagree.
- **Why it matters:** Position lifecycle directly drives stop management, exit advisories, and `rme_events` audit semantics. Undefined states produce undefined behaviour in the very subsystem the ADR is trying to make deterministic.
- **Concrete fix:** Either (a) add `ExitPending` to `REQ-PLC-002` with a clean transition table, or (b) rewrite the architecture/ADR wording to use only the five canonical states (the exit-advisory condition should be a flag on `Open`, consistent with `portfolio-risk-guidelines.md` which already models exit advisory as a flag). Option (b) is the smaller change and preserves the risk-guidelines model. `Submitted`/`Filled` hops should similarly either be documented as states or removed.

### CG-3. `sys_config` seed table is incomplete; REQ-CONFIG-009 is violated

- **Location:** `docs/system-config.md` seed table vs references scattered through `docs/requirements-spec.md`, `docs/system-architecture.md`, and ADRs.
- **What is missing (non-exhaustive):**
  - `orders.intent_timeout_minutes` (architecture reconciliation path)
  - `rme.channel.backlog_warn_depth` (REQ-RME-CONC-004)
  - `jobs.admin_token_check.daily_check_time` (REQ-NOTIFY-022)
  - `jobs.ledger_lock.ttl_seconds`, `jobs.ledger_lock.renewal_interval_seconds`, `jobs.ledger_lock.skip_warn_threshold` (REQ-PORT-031)
  - `risk.corporate_action.avg_cost_delta_threshold_pct` (ADR-0004, REQ-PORT-016)
  - `risk.circuit_limit_stale_minutes` (REQ-ORDER-018a)
  - `risk.equity_override_stale_days` (REQ-RME-006c)
  - `strategies.backtest.survivorship_bias_discount_pct` (REQ-STRAT-011b)
  - `strategies.backtest.draft_expiry_days` (REQ-BTSTORE-009a)
  - `jobs.lmds.capacity_warn_pct` (REQ-SLO-011)
- **Why it matters:** REQ-CONFIG-009 explicitly requires a seed value when a key is introduced. Without seeds, startup readers will hit missing-key paths on first boot, and admins will have no visible record in `sys_config` until something writes a value. Several of these keys (intent timeout, ledger lock TTL) gate correctness-critical code paths.
- **Concrete fix:** Extend the seed table in `docs/system-config.md` with each missing key, a safe default, the tier it lives in, and the requirement or ADR that introduced it. Add a CI check (later) that scans doc references to `sys_config.*` and fails the build if any referenced key lacks a seed entry.

### CG-4. RME single-instance assumption has no runtime enforcement

- **Location:** `docs/adr/0003-rme-per-position-event-serialisation.md` "Scale context" (single instance in Phase A) and Decision (Option C "single-instance only; breaks silently and catastrophically under horizontal scaling").
- **What is wrong:** The ADR's own rejection of Option B acknowledged that Option C "breaks silently and catastrophically under horizontal scaling." No requirement, startup check, or deployment guardrail prevents or detects accidental multi-instance deployment.
- **Why it matters:** Worker Service scaling decisions are often made operationally (Azure autoscale rules, blue/green overlap, restart-before-terminate). A single extra instance running for minutes can corrupt position state in ways not detectable by OCC alone (same process, different channels, both read current `_version`, both write with that version sequentially — only the last write is safely ordered from MongoDB's perspective, but the ordering between events on the two instances is lost).
- **Concrete fix:** Add a requirement that the Worker Service must acquire an exclusive "rme-primary" lease in Redis (or MongoDB) at startup; secondary instances must self-downgrade to a standby state that does not consume RME events. On lease loss, the active instance must stop processing events cleanly. Document this as the Phase A/B enforcement and clarify that the Phase C extension in ADR-0003 replaces the lease with the partition claim mechanism already described.

### CG-5. Crash-recovery event replay is incomplete

- **Location:** ADR-0003 Consequences ("any event they generated will be re-detected and re-enqueued on the next poll cycle").
- **What is wrong:** Only LMDS (price breach) and LADS (fill) events are generated by short-interval polling. Other event producers listed in the ADR's own Context table — EOD stop-level update, portfolio heat recalculation on drawdown threshold crossing, admin kill switch, user suspension, EODSR supersession of a PendingEntry — are not re-detected by pollers. If such an event is enqueued and the worker restarts before it is consumed, it is lost permanently.
- **Why it matters:** The two losses that matter most for financial safety are admin kill-switch and drawdown-threshold-crossed: both are protective events. Silently dropping either on a restart leaves the RME operating as if the protective condition never occurred.
- **Concrete fix:** Require durable enqueue for admin/drawdown/EOD events (persist the intended event to a MongoDB `rme_pending_events` collection with an idempotent consumer) or require the channel consumer to write an "event_received" audit record before processing, so that a restart recovery pass can detect unfinished events and re-emit them. Either approach should become a new REQ-RME-CONC-006 and be called out in the Phase 6 migration plan.

### CG-6. Config and secrets tier startup failure modes are undefined

- **Location:** `CLAUDE.md` ("Never make startup-critical configuration depend only on MongoDB"); `docs/system-architecture.md` describes three tiers but not startup ordering/failure behaviour; `docs/system-config.md` is silent on unreachable-at-boot paths.
- **What is wrong:** The docs do not specify what the backend API and Worker Service do when Azure App Configuration or Key Vault is unreachable at startup, nor whether `sys_config` is read at startup or lazily on first use, nor the cache/fallback lifetime. Guardrail coverage is asymmetric (MongoDB-only is forbidden; App-Config-only is not forbidden or guarded).
- **Why it matters:** A brief Azure App Configuration outage during a pod restart could leave a mis-configured service running with unknown defaults, or fail to start at all with no documented retry/back-off behaviour.
- **Concrete fix:** Add requirements that specify (a) ordering: Key Vault → App Configuration → `sys_config`; (b) minimum-viable-startup semantics — which settings must succeed or the service must refuse to start, which can fall back to a last-known-good local cache, which can be lazily loaded; (c) max staleness for cached App Config values; (d) observability: emit an alert when any tier is degraded.

### CG-7. Timeframe coherence is asserted but not architecturally enforced

- **Location:** `CLAUDE.md` guardrails ("Never let timeframe logic diverge across charting, backtesting, and the EOD Signal Runner"); `docs/system-architecture.md` references the "timeframe and calendar layer" but does not name a single library/interface or module that all three paths consume.
- **What is wrong:** The guardrail exists; the architectural constraint that makes violation impossible does not. `charting` serves candles to the Next.js frontend from SQL; `backtesting` reads from `D_/W_/M_` SQL tables plus rolling-window derivations; EODSR reads the same sources but runs on a different cadence. Each is free to implement its own "what is a session" and "what is a weekly candle" helper unless a shared module is mandated.
- **Why it matters:** Divergence here corrupts Signal parity — the single largest reason `REQ-STRAT`-family tests exist.
- **Concrete fix:** Add a requirement that names a single .NET module (e.g., `SignalStack.MarketCalendar`) as the sole source of session boundaries, candle construction, and holiday handling, and that backtest, EODSR, charting candle-range endpoints, and the timeframe selector all consume that module (no local duplicates, enforced by code-review check in engineering-standards).

### CG-8. Corporate action detection gate for Phase 5 is not in the roadmap

- **Location:** `docs/adr/0004-corporate-action-detection-threshold.md` status "Proposed — threshold requires calibration before Phase 5"; `docs/implementation-roadmap.md` Phase 5.
- **What is wrong:** The ADR gates itself on calibration "before Phase 5 implementation begins." The roadmap does not echo this gate. A Phase 5 task runner who reads only the roadmap will proceed with the 50% default that the ADR itself labels unreliable for 2:1 splits and 1:1 bonuses (exactly at boundary) and missed for 3:2 bonus and rights issues.
- **Why it matters:** Phase 5 builds portfolio accounting on top of this detector. If the threshold is wrong, stop levels and R calculations silently corrupt for any user holding a stock that had a bonus/rights action between entry and sync.
- **Concrete fix:** Add an explicit Phase 5 precondition in the roadmap referencing ADR-0004; make the calibration task a roadmap deliverable; block Phase 5 merge if ADR-0004 is still `Proposed`.

---

## Logical inconsistencies

### LI-1. Redis is correctness-critical in one place, non-correctness in another

- **Location:** ADR-0003 ("Redis is not a correctness dependency for Phase A/B — if Redis fails, WebSocket fan-out and caching degrade but RME event processing continues") vs `REQ-PORT-031` (trade-ledger writes require a Redis lock).
- **What is wrong:** If Redis is unavailable, RME processing continues (per ADR-0003) but the FIFO trade-ledger writes that RME ultimately depends on for `positions.avg_buy_price` must not proceed (per REQ-PORT-031). The RME will therefore operate on a position whose quantity and cost basis could be stale or mid-write.
- **Why it matters:** The "RME degrades gracefully during Redis outage" narrative is misleading.
- **Concrete fix:** Either downgrade REQ-PORT-031's lock to an OCC pattern (matching the positions collection model) or upgrade the Redis failure stance in ADR-0003 to "Redis is a correctness dependency for any write that touches the trade ledger; the RME must stop consuming events that depend on fresh ledger state." Pick one and make it consistent.

### LI-2. Watchlist behaviour during FYERS session failure

- **Location:** `REQ-WATCH-005` implies watchlist shows last-known-stale price when the user's FYERS token is locked/dirty; `REQ-SESSION-003` / `REQ-SESSION-004` block FYERS-dependent features when the user's session is invalid.
- **What is wrong:** These two requirements describe different outcomes for the same failure mode (user token unusable): one says show stale, the other says block.
- **Concrete fix:** Specify which state (token dirty vs session invalid) produces which behaviour; one is almost certainly a subset of the other.

### LI-3. `CNC` product type claim vs architecture attribute surface

- **Location:** `REQ-ORDER-010` mandates CNC product type; `docs/system-architecture.md` attribute surface section lists `data-product` value as `INTRADAY confirmed` with CNC "presumed."
- **What is wrong:** REQ is CNC-mandatory; architecture lists INTRADAY as the confirmed value. The widget parameter that actually goes to FYERS will reflect whichever is implemented.
- **Concrete fix:** Re-confirm the widget attribute value against FYERS documentation and correct the architecture attribute surface to match REQ-ORDER-010 (or raise a REQ change if CNC is not actually the desired product).

### LI-4. `REQ-STOP-003` time stop default vs `sys_config` seed

- **Location:** `docs/system-config.md` seed has `risk.time_stop.default_sessions = 10`; `REQ-STOP-003` says the session count is "configured per Signal Subscription" and does not specify a platform default.
- **What is wrong:** The seed implies a platform default; the REQ implies no platform default exists.
- **Concrete fix:** Clarify: is the seed the fallback if a Signal Subscription omits the value, or should the Signal Subscription always require an explicit value? Document the answer and align the seed.

### LI-5. "Intent ledger" is referenced but not a data collection

- **Location:** `docs/system-architecture.md` references `intent_ledger`; `docs/data-management.md` does not list it.
- **What is wrong:** Either the collection exists (and data-management must list its lifecycle, TTL/archive, fields, indexes) or the architecture's reconciliation design is unimplementable.
- **Concrete fix:** If CG-1 is resolved in favour of the intent-ledger model, add the collection to `docs/data-management.md` with fields covering: `user_id`, `intent_id`, `symbol`, `side`, `qty`, `product_type`, `order_type`, `created_at`, `status` (`pending|matched|expired|reconciled_missing`), `matched_broker_order_id`, `matched_at`, `last_polled_at`. Specify TTL (match the `intent_timeout_minutes` config) and archive path.

---

## Underspecified areas

### US-1. Widget-to-backend parameter contract

- **Location:** `docs/system-architecture.md` FYERS API Connect section.
- **What is missing:** The concrete list of HTML `data-*` attributes the platform sets on the branded-button element, their validation rules (symbol format, qty range, order type enum, product type enum, price precision), and the server-side validation path that signs/verifies them before render.
- **Why it matters:** Without this, the frontend and backend will ship mismatched attribute names, the widget will silently pop up with wrong values, and users will click through to execute unintended orders.
- **Fix:** Add a sub-section under REQ-ORDER that enumerates the attribute contract, with a TypeScript interface shown in docs as the source of truth, and require the backend to return all parameters in a single signed blob the frontend binds 1:1 to attributes.

### US-2. Admin FYERS token refresh failure scope

- **Location:** `REQ-NOTIFY-022` notifies on failed daily admin token refresh; no doc specifies what DataSync, HistoricDataSeed, or EODSR do when the admin token is dirty.
- **What is missing:** Per-job behaviour on admin token failure (retry, back-off, skip-with-alert, use last good token until expiry), whether a manual admin token refresh through the portal is available, and reconciliation behaviour when the token eventually refreshes (do missed jobs backfill?).
- **Fix:** Add requirements under each background job describing admin-token-unavailable behaviour and the backfill/catch-up pattern.

### US-3. Symbol Validity Probe resolution states

- **Location:** `docs/data-management.md` `symbol_health`; `REQ-UNIV-021`.
- **What is missing:** The `status` field uses `ok | flagged | resolved`, but the transitions and the action that sets `resolved` (admin dismissal? successful probe after a flag?) are not spelled out. The retention of `resolved` records is "retained … for audit" with no TTL/archive entry in the summary table.
- **Fix:** Add a transition table (ok → flagged on consecutive failures ≥ threshold; flagged → resolved by admin action or N consecutive successes; resolved → ok on next successful probe), and specify retention (never archived, never TTL'd, or archived N days after `resolved_at`).

### US-4. Backtest universe reconstruction

- **Location:** `REQ-STRAT-011b` applies a flat 15% expectancy discount "for survivorship bias"; no doc specifies reconstructing the historical Nifty 500 membership at each backtest timestep.
- **What is missing:** Whether the backtest engine uses the current symbol universe for a historical run (guaranteeing look-ahead/survivorship bias) or reconstructs the universe from archived symbol-master + join-date/archive-date.
- **Fix:** Either build proper historical universe reconstruction and remove the discount, or document that the 15% is a known-approximate overlay and add a hard precondition that backtests flag runs where the symbol universe materially changed during the test window.

### US-5. Azure tier assignment for concrete settings

- **Location:** `docs/system-config.md`, `docs/system-architecture.md`.
- **What is missing:** A per-setting tier column: (1) Azure App Configuration / (2) Key Vault / (3) MongoDB `sys_config`. Several items could plausibly live in either App Configuration (shared tech) or `sys_config` (admin-managed), and the docs do not disambiguate.
- **Fix:** Add a third column "Tier" to the seed table in `system-config.md` with explicit assignment for every row, and add the same assignment inline for every technical-config key in App Configuration.

### US-6. Position → channel lifecycle handshake

- **Location:** `REQ-RME-CONC-005` says the channel is created "immediately when a new PendingEntry position document is written"; the architecture handoff from EODSR (which writes `entry_signals`) to RME (which creates a position document) is not explicit.
- **What is missing:** Who owns the transition `entry_signals → positions(PendingEntry)`? Is it the RME consumer or an EODSR follow-up step? This determines whether the channel exists at the moment the first event is enqueued.
- **Fix:** Add a sequence diagram or numbered flow to `docs/system-architecture.md` under "RME Signal and Advisory Flow" starting at EODSR emit and ending at PendingEntry + channel created, naming the component that writes the position document.

---

## Edge cases and failure modes not addressed

### EC-1. Redis eviction of the `trade_ledger` write lock

- **Location:** `REQ-PORT-031`.
- **What is missing:** Behaviour if Redis evicts the lock (memory pressure) or if the lock holder's renewal thread stalls (GC pause, CPU starvation). With `ttl_seconds` + `renewal_interval_seconds` configurable but not reasoned about as a pair, it is possible to pick values that allow split-brain writes.
- **Fix:** Add invariants: `renewal_interval_seconds < ttl_seconds / 3`; require a server-side fencing token (Redlock-style) rejected by the ledger writer on stale holder attempts.

### EC-2. FYERS account data sync partial failures

- **Location:** `fyers_account_sync` collection; `REQ-PORT` family.
- **What is missing:** What LADS does when FYERS returns partial data (e.g., trades API returns 200, holdings returns 500). The RME may act on reconciled fills without a refreshed holding to cross-check.
- **Fix:** Add requirement that partial syncs are atomic per sync type (trade, holding, order) but that fills detected without a matching holding refresh are held in a pending reconciliation queue until holding sync succeeds.

### EC-3. `trading_calendar` drift from real exchange schedule

- **Location:** `docs/data-management.md` `trading_calendar` — future sessions "seeded from exchange holiday schedules and corrected by admin as needed."
- **What is missing:** Behaviour when a surprise exchange holiday (e.g., national event) invalidates already-computed `time_stop_date` values or EOD candle boundaries. No recompute-on-calendar-change path is specified.
- **Fix:** On `trading_calendar` update, trigger a recompute job that updates `time_stop_date` on all open positions and surfaces an admin advisory if any EODSR result depended on the stale calendar.

### EC-4. FYERS widget pop-up blocked by browser

- **Location:** `REQ-ORDER-017a` covers `finished` callback timeout; popup-blocked vs simply-never-opened may look identical to the user but should be handled distinctly.
- **What is missing:** Client-side detection of popup blocker state before rendering the button, and a user-facing remediation path.
- **Fix:** Require a pre-flight popup-availability check and surface a pre-emptive banner instructing the user to allow popups.

### EC-5. OAuth provider (Google/Microsoft/Facebook) outage on login

- **Location:** `CLAUDE.md` defaults; no dedicated failure-mode doc.
- **What is missing:** Whether multi-provider OAuth is additive (any one is enough) or exclusive (provider selected at signup). If additive, provider outage is recoverable by the user choosing another; if exclusive, it is not. No requirement says which.
- **Fix:** Pick and document: user's primary OAuth provider is recorded; sign-in with another provider performs an account-link check against email. Or, require users to set up two providers at onboarding.

### EC-6. FYERS token expiring mid-chart-view

- **Location:** PLD (Portal Live Data) path; `REQ-SESSION-003/004`; `REQ-WATCH-005`.
- **What is missing:** Behaviour of the live WebSocket / REST polling path when the token expires while a chart is open. Does the page fall back to SQL-served stale candles, show a re-auth prompt, or silently go blank?
- **Fix:** Specify: on detected 401 from FYERS, disconnect the WS, fall back to SQL historical candles, surface a re-auth CTA, and resume live feed on successful re-auth.

### EC-7. `portfolio_snapshots` overwrite losing in-flight computation

- **Location:** `docs/data-management.md` `portfolio_snapshots` ("overwritten in place on each update; the previous snapshot is not retained").
- **What is missing:** Concurrency protection for two near-simultaneous snapshot writes (user clicks manual refresh while LADS completes). No version/OCC described.
- **Fix:** Add a `_version` field and OCC on `portfolio_snapshots`, matching the `positions` pattern, so a stale LADS-completion does not clobber a user-initiated refresh.

### EC-8. Single active session eviction UX

- **Location:** `REQ-SESSION-002`, `docs/data-management.md` `sessions`.
- **What is missing:** What the previous tab sees when a new login invalidates its session. The TTL is 24h but on-new-login invalidation is immediate per the collection description.
- **Fix:** Specify the server behaviour on API calls from the invalidated session (401 + `session_superseded` reason code) and the frontend behaviour (redirect to login with explanatory banner).

---

## Questions for the author

1. Which FYERS API Connect return model is authoritative: `finished` callback (requirements + roadmap) or `intent_ledger` + LADS reconciliation (architecture)? The other document must be corrected.
2. Is `ExitPending` a real position state that `REQ-PLC-002` is missing, or architecture shorthand for an `Open` position with an exit advisory flag? If the latter, please remove the word from architecture and ADR-0003.
3. Is `intent_ledger` a real MongoDB collection? If yes, please add it to `docs/data-management.md` with full lifecycle and TTL/archive policy.
4. What is the intended deployment count of the Worker Service across Phase A/B, and what mechanism prevents an accidental second instance from corrupting RME state?
5. On Worker Service crash while channels hold un-consumed events, which event sources are expected to re-emit and which are lost? In particular: admin kill switch, user suspension, EOD stop update, drawdown-state-change — are any of these durable today?
6. For each of the ten missing `sys_config` keys listed in CG-3, what is the intended default and tier?
7. Which concrete module is the single source of timeframe and calendar logic for charting, backtesting, and EODSR? If it does not exist yet, which Phase introduces it?
8. Does the backtest engine reconstruct the historical Nifty 500 universe, or does the 15% expectancy discount (REQ-STRAT-011b) remain the only survivorship-bias mitigation?
9. Is the RME's Redis-optional stance in ADR-0003 compatible with `REQ-PORT-031`'s Redis-mandatory trade-ledger lock? If yes, the RME must stop consuming events when the ledger path is down — where is that coupling documented?
10. What is the admin-token-unavailable behaviour for each background job (DataSync, HistoricDataSeed, EODSR, LMDS, LADS, Notification Delivery, Symbol Validity Probe) and does any of them backfill after recovery?
11. What is the startup-time contract when Azure App Configuration is unreachable? Hard-fail, use last-known-good cache, lazy-load?
12. Is ADR-0004 (corporate action threshold) blocking Phase 5, and should the roadmap be updated to call that out?
13. What is the popup-blocked UX for the FYERS branded button, and is there a pre-flight detection requirement missing from the spec?
14. For single-active-session eviction, does the platform emit a specific reason code so the frontend can show an informative error instead of a generic 401?
