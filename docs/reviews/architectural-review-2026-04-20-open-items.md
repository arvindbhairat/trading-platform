# Architectural Review — Open Items

**Reviewer role:** Senior systems architect, technical coherence only (compliance out of scope)
**Source review:** `docs/reviews/architectural-review-2026-04-18.md` (all 7 Critical Gaps resolved 2026-04-18/19)
**Carry-forward date:** 2026-04-20
**Scope:** `CLAUDE.md`, `docs/README.md`, `docs/terminology.md`, `docs/requirements-spec.md`, `docs/product-scope.md`, `docs/system-architecture.md`, `docs/implementation-roadmap.md`, `docs/engineering-standards.md`, `docs/system-config.md`, `docs/portfolio-risk-guidelines.md`, `docs/data-management.md`, `docs/repo-structure.md`, `docs/adr/0001–0004`, `docs/operations/fyers-api-budget.md`, `docs/operations/runbooks/*`.

## Purpose of this document

The 2026-04-18 architectural review raised 7 Critical Gaps, 5 Logical Inconsistencies, 11 Underspecified Areas, 10 Edge Cases, and 15 Open Questions. The 7 Critical Gaps and 1 Underspecified Area (post-callback portal state, resolved alongside CG #2) are closed and retained in the original review as an audit trail.

This document carries forward the remaining items — re-stated precisely, cross-referenced to the canonical docs, and extended with a recommendation per item. Findings are grouped by severity class:

- **Logical Inconsistencies** — two or more docs state things that cannot both be true.
- **Underspecified Areas** — a requirement or design decision is missing; silent runtime behaviour is load-bearing.
- **Edge Cases and Failure Modes** — a specific failure path is undefined or ambiguous.
- **Open Questions** — deliberately left as questions for the author; some overlap with items above and are cross-referenced.

Items originally listed in the Summary of the 2026-04-18 review (bullets #8, #9, #10) have been folded into the relevant sections below; bullet #1 was resolved as a misread during CG #1 and is omitted.

---

## Logical Inconsistencies

### LI-1 — Exit Priority ordering contradicts drawdown threshold semantics ✅ RESOLVED 2026-04-20

- **Where:** `docs/portfolio-risk-guidelines.md` § Exit Priority (list) vs. § Drawdown Response Thresholds (table).
- **Observation:** "Drawdown control exit" is ranked #5 in Exit Priority, implying it can terminate a position. The Drawdown Response Thresholds table, however, lists every threshold from < 10 % up to ≥ 30 % as advisory, not as a state-changing exit — even the ≥ 30 % "stop all trading" row is phrased as "surface advisory." The two sections cannot both be true: either drawdown is an exit (Exit Priority) or it is not (threshold table).
- **Recommendation:** Pick one semantic and align the other. Recommended shape: keep the threshold table as the authoritative source (it is more granular), remove "Drawdown control exit" from the Exit Priority list, and promote the ≥ 20 % row to "block new entries and surface mandatory exit advisory" so the top end of the drawdown ladder carries visible weight without introducing a new lifecycle-terminating state. Record the choice in a short ADR note because it affects the RME's public semantics.

### LI-2 — CNC enforcement vs. user "Market/Limit" framing in the Phase 1 modal ✅ RESOLVED 2026-04-20

- **Where:** `docs/implementation-roadmap.md` Phase 7 build item 3 ("Market/Limit order type selection … CNC product type enforced") vs. `docs/product-scope.md` "user-initiated execution assistance only" / REQ-ORDER-010 (CNC enforcement).
- **Observation:** The Phase 1 modal offers the user `Market` vs. `Limit` but hides the `CNC` vs. `INTRADAY` choice. A user who wants to exit same-day will be pushed into placing a manual order directly at FYERS — a predictable UX trap for anyone used to broker-side modals. The product-scope decision to enforce CNC is correct (REQ-ORDER-010a/b now formalise this); the modal framing is what drifts.
- **Recommendation:** Add a static notice in the Phase 1 modal ("This order is placed as CNC — for swing / position trades. Same-day exits must be initiated at FYERS directly") and cross-reference it from REQ-ORDER-010. Do not expose product-type selection in the modal — that would reopen REQ-ORDER-010 and violate the CNC enforcement guardrail.

### LI-3 — `rme_events` is described as an immutable audit log but used for concurrency incident flags ✅ RESOLVED 2026-04-20

- **Where:** `docs/adr/0003-rme-per-position-event-serialisation.md` § Decision ("position is flagged for admin review in `rme_events` with reason `concurrency_conflict_unresolved`") vs. `docs/data-management.md` description of `rme_events` as an immutable audit log.
- **Observation:** The same collection is being asked to hold two different classes of record with different retention, query, and ops-alert semantics: (a) time-series audit entries that must be retained for post-mortem and compliance, and (b) operator-action flags that should fan out to the admin UI and drive an incident-resolution workflow. Mixing them muddles TTL policy, complicates admin dashboards, and makes retention cost unpredictable.
- **Recommendation:** Route `concurrency_conflict_unresolved` (and analogous flag-class records) to a dedicated `rme_incidents` collection, or to the existing `audit_events` collection if its semantics fit. Keep `rme_events` strictly event-sourced. Update REQ-RME-CONC-002 and ADR-0003 to name the target collection explicitly.

### LI-4 — 80 % FYERS daily-budget projection window is undefined ✅ RESOLVED 2026-04-20

- **Where:** `docs/operations/fyers-api-budget.md` § Sensitivity + REQ-RATE-012.
- **Observation:** REQ-RATE-012 blocks `sys_config` saves that would breach the 80 % daily-budget threshold, but neither the requirement nor the budget doc specifies whether the projection is (a) "full day going forward at the new settings" (worst-case, admin-unfriendly — an admin save mid-session is penalised for consumption that already happened), (b) "already-consumed + projected remainder at the new settings" (admin-friendly, reflects reality), or (c) something else. The math diverges materially between the two.
- **Recommendation:** Amend REQ-RATE-012 to specify projection (b) — already-consumed calls in the current UTC calendar day plus projected calls for the remainder of the day under the new settings. Record the budget reset boundary (UTC midnight vs. IST market close vs. FYERS vendor reset) in `fyers-api-budget.md` alongside the calculation. This also calls for a matching `sys_config` key naming the reset boundary so future providers (TrueData, GDF) can override it without a code change.

### LI-5 — Channel lifecycle ambiguity around synchronous PendingEntry → Rejected ✅ RESOLVED 2026-04-20

- **Where:** `docs/adr/0003-rme-per-position-event-serialisation.md` REQ-RME-CONC-005(a) and (b).
- **Observation:** The ADR creates a per-position channel on PendingEntry document write and destroys it on Rejected. RME pre-trade validation may move PendingEntry → Rejected synchronously inside the same request (e.g., heat breach, drawdown gate fail, equity-base unavailable). In that path the channel is created and destroyed before any event is enqueued, and the channel registry has to handle a "shortest-possible lifetime" case that serves no purpose.
- **Recommendation:** Adopt lazy channel creation — create the channel on first event enqueue rather than on position document creation. Rejected positions that never produced an event simply never register a channel. Update REQ-RME-CONC-005 to reflect the lazy policy and add a contract test for the "rejected on first validation" path.

---

## Underspecified Areas

### UA-1 — Duplicate position prevention at the (symbol, user, signal subscription, decision window) level ✅ RESOLVED 2026-04-20

- **Where:** `docs/adr/0003-rme-per-position-event-serialisation.md` (per-position channel key is position id); `docs/requirements-spec.md` REQ-SIG / REQ-PLC families; previously flagged as Summary bullet #8 and Edge Case #5 in the 2026-04-18 review.
- **Observation:** The per-position channel serialises events on an existing position, not position creation. Two nearly-simultaneous entry signals for the same (user, symbol, signal subscription) — e.g., EODSR evaluating an overlapping decision window, or a retry of a flaky post-callback write — can produce two PendingEntry documents that both look valid. The RME will then allocate two channels and treat them as independent positions. This is the single most likely silent-correctness failure in the current design.
- **Recommendation:** Add a new requirement (REQ-PLC-001a or similar) declaring a uniqueness invariant on the tuple (`user_id`, `symbol`, `signal_subscription_id`, `decision_window_id`) at position create time, enforced by a MongoDB partial unique index on non-terminal lifecycle states (PendingEntry, Open, Suspended). A second concurrent creation must be rejected with `duplicate_active_position_attempt`. Update REQ-RME-CONC-005 to note that channel allocation happens only after the uniqueness check passes. Add a contract test for the concurrent-create race.

### UA-2 — Trading-calendar-change impact on existing `time_stop_date` ✅ RESOLVED 2026-04-20

- **Where:** `docs/requirements-spec.md` REQ-STOP-003; `docs/portfolio-risk-guidelines.md` § Stop Loss Types (Time Stop). Overlaps Edge Case EC-2.
- **Observation:** Admin edits the internal trading calendar. Open positions already carry a `time_stop_date` computed from the pre-edit calendar. No rule says whether those dates are recomputed, and if so, on what rule; a calendar edit that introduces a new holiday (e.g., an unscheduled market closure) may leave existing positions with a `time_stop_date` on a day that is now a holiday.
- **Recommendation:** Add REQ-STOP-003a: on every calendar edit, the EODSR (or a dedicated recompute job) recomputes `time_stop_date` for every non-terminal position using the new calendar, applies a roll-forward-to-next-trading-session rule for dates landing on holidays, writes an advisory entry on each affected position document, and emits a single admin summary notification of the recompute scope. Record the trigger path (admin save, post-commit) in `docs/system-architecture.md`.

### UA-3 — OAuth provider outage UX ✅ RESOLVED 2026-04-20

- **Where:** `docs/requirements-spec.md` REQ-AUTH family.
- **Observation:** Three OAuth providers are supported (Google, Microsoft, Facebook/Meta). If one is down, fallback behaviour is undefined — does the login page hide it, mark it "temporarily unavailable," or fail the whole sign-in attempt silently? For an existing user bound to a specific provider, is cross-provider fallback allowed (security risk: account hijack via a second provider under the same email), or is it forbidden?
- **Recommendation:** Specify: (a) login page always offers all three providers; (b) any one success is sufficient for a new sign-in; (c) for an existing user, the bound provider is fixed — no fallback between providers for the same account, even if emails match; (d) if a user's bound provider is unreachable, surface a dedicated "provider unreachable — retry later or contact admin for account migration" screen rather than failing silently. Add REQ-AUTH-00x codifying the above and update the login runbook.

### UA-4 — Azure App Configuration startup-failure behaviour ✅ RESOLVED 2026-04-20

- **Where:** `docs/adr/0002-configuration-layering.md` § Decision; `CLAUDE.md` guardrail "startup-critical configuration must not depend only on MongoDB."
- **Observation:** Bootstrap configuration layers are Azure App Configuration → environment variables → Key Vault. If App Config is unreachable at API or Worker startup, the spec does not declare whether the service (a) refuses to start, (b) starts with a last-known-good cache, (c) starts with embedded defaults, or (d) degrades in some undocumented way. For the Worker, whose singleton constraint depends on reaching Redis for the lease, startup behaviour under partial config-source outages compounds the problem.
- **Recommendation:** Amend ADR-0002 with an explicit policy: warm-start from a local last-known-good cache if App Config is unreachable, log a structured warning, emit an OTEL metric `config.source.last_reached_seconds`, and hard-fail startup only if no cache exists. Write the cache on every successful App Config read. Specify cache location (process-local directory, not MongoDB, to preserve the guardrail). Add a corresponding seed key `config.last_known_good.max_age_seconds` (e.g., default 86400) so stale caches fail closed.

### UA-5 — Survivorship bias discount mechanism ✅ RESOLVED 2026-04-20

- **Where:** `docs/requirements-spec.md` REQ-STRAT-011b.
- **Observation:** REQ-STRAT-011b names a survivorship discount but does not declare how it is applied — multiplicative on returns, additive annotation on the backtest summary, filter on trade count, or something else. Downstream consumers (backtest result screens, RME-informed strategy comparison) cannot be built deterministically against this.
- **Recommendation:** Declare explicitly: the discount is an informational annotation on the backtest summary screen (e.g., "historical universe coverage: 87 % — results may be optimistic by up to X bps"), never applied to the numeric result fields. Rationale: a multiplicative discount bakes a bias estimate into the result that future reviewers cannot unwind; an annotation preserves the raw number and makes the caveat visible. Extend REQ-STRAT-011b and add a contract test asserting the annotation appears on every backtest whose universe coverage is below a threshold seeded in `sys_config`.

### UA-6 — `portfolio_snapshots` cache staleness ✅ RESOLVED 2026-04-20

- **Where:** `docs/requirements-spec.md` REQ-DASH-015 / 016; `docs/data-management.md` § portfolio_snapshots.
- **Observation:** Snapshot cache TTL and invalidation triggers are not declared. A confirmed FYERS fill should invalidate the cached snapshot, but no requirement says so, and the dashboard read path may therefore surface stale equity-curve or heat figures for an undefined window.
- **Recommendation:** Add REQ-DASH-017 declaring invalidation on: (a) every LADS cycle that writes a new trade record; (b) every manual admin adjustment; (c) EOD close of each session. Specify a TTL ceiling in `sys_config` (e.g., `dashboard.portfolio_snapshot.ttl_seconds` default 900) so a missed invalidation cannot stall a stale view indefinitely. Cross-reference the new REQ-PORT-031a `ledger_snapshot_version` (CG #7 resolution) so the dashboard cache key includes the version — stale caches become unreachable by construction.

### UA-7 — Telegram delivery idempotency key ✅ RESOLVED 2026-04-20

- **Where:** `docs/requirements-spec.md` § Notifications; `docs/data-management.md` § notifications.
- **Observation:** Retry with back-off is specified but no idempotency key is named. A crash after the HTTP send but before the MongoDB ack can double-deliver on retry — the user gets the same alert twice, and our audit trail has no basis to dedupe.
- **Recommendation:** Require the Notification Delivery Job to compute a deterministic `client_message_id` (Telegram supports this as a per-chat idempotency field through careful use of message content + local id) or, more robustly, persist the returned `telegram_message_id` before acknowledging the work item. Add REQ-NOTIFY-00x codifying the flow: generate deterministic id from `notifications._id` → send → persist Telegram's returned id → ack. On retry, re-sending with the same deterministic id is either idempotent at Telegram's side (preferred) or detected by the pre-send check of the persisted Telegram id.

### UA-8 — Global kill-switch scope ✅ RESOLVED 2026-04-20

- **Where:** `docs/portfolio-risk-guidelines.md` § Operational Risk Controls; `docs/requirements-spec.md` REQ-ADMIN family.
- **Observation:** "Suspend all strategy signal generation platform-wide" is the documented effect, but it is unclear whether LMDS stop monitoring continues, whether LADS continues, and whether PLD-driven live chart data continues to render. A user with an open position in a fast-moving market cares deeply about the answer.
- **Recommendation:** Specify scope explicitly: kill switch halts only EODSR entry-signal emission. LMDS stop monitoring, LADS, exit advisories from RME, PLD live quotes in the user chart, and the Notification Delivery Job all continue. Add REQ-ADMIN-00x codifying the scope, plus a visible admin-UI description of what the switch does and does not stop, to prevent a future operator from treating it as a full-platform halt.

### UA-9 — Multi-tab session behaviour for the same user ✅ RESOLVED 2026-04-20

- **Where:** `docs/system-architecture.md` § Session / FYERS Lock.
- **Observation:** Two browser tabs on the same user — undefined whether the FYERS session lock allows both to hold a PLD WebSocket stream, or the second claim evicts the first, or they share. This affects FYERS token rate consumption per user (the WebSocket is per-user-token) and the UX of someone who left a tab open overnight.
- **Recommendation:** Adopt latest-tab-wins semantics backed by a per-user-token session registry in Redis. On a new PLD subscription, write a session id under `session:fyers:{user_id}` with TTL from `sys_config.session.fyers.lease_ttl_seconds`; evict earlier sessions with a WebSocket close code and an in-portal banner "this tab was disconnected because another tab on your account opened a live chart." Add REQ-SESSION-00x and a seed key.

### UA-10 — ADR-0004 calibration ownership and due date ✅ RESOLVED 2026-04-20

- **Where:** `docs/adr/0004-corporate-action-detection-threshold.md` § Decision.
- **Observation:** Calibration of the corporate-action detection threshold is gated on Phase 5, but no owner, sample source, or deliverable date is recorded. An ADR whose decision depends on a future calibration pass but names no owner for that pass will slip silently.
- **Recommendation:** Update ADR-0004 § Decision with: owner (role, e.g., "Quant lead" or "Platform engineering lead"), sample window (NSE corporate actions over a specified 24-month period — a concrete date range), deliverable form (a calibration note committed to `docs/adr/0004-calibration-note-YYYYMMDD.md`), and a date expressed as a Phase 5 entry precondition in `docs/implementation-roadmap.md`. Phase 5 must not start until the calibration note is merged.

---

## Edge Cases and Failure Modes

### EC-1 — Admin FYERS token refresh fails mid-session ✅ RESOLVED 2026-04-20

- **Where not addressed:** No binding in `docs/requirements-spec.md` between admin token state and the subsystems that consume it (LMDS, DataSync, HDS, EODSR).
- **Observation:** If the admin's daily FYERS token expires mid-day or refresh fails at market open, LMDS stop monitoring and DataSync go dark (they use the admin token via the MDP abstraction). LADS continues (per-user tokens). The platform then runs in an asymmetric degraded state: users see live quotes through their own tokens but receive no stop-breach alerts, with no admin-facing indicator.
- **Recommendation:** Add REQ-MARKET-00x: on any failure of the shared-ingestion token refresh, LMDS and DataSync suspend, EODSR skips its next entry-signal evaluation, and an admin-dashboard banner + Telegram admin alert fires ("shared-ingestion suspended: token refresh failed — last success at …"). LADS continues unaffected. Resume is gated on the next successful token fetch. Record the asymmetric-degradation semantics in `docs/system-architecture.md`.

### EC-2 — Time stop landing on a now-non-trading day ✅ RESOLVED 2026-04-20

- **Where:** `docs/requirements-spec.md` REQ-STOP-003; `docs/portfolio-risk-guidelines.md` § Stop Loss Types (Time Stop). Overlaps UA-2.
- **Observation:** Calendar is edited; a position's `time_stop_date` lands on what is now a holiday. Without a recompute rule (UA-2), the time stop either fires on a non-trading day (does nothing observable), or is interpreted inconsistently across RME / EODSR.
- **Recommendation:** Solved as part of UA-2. The specific edge-case resolution is: roll forward to the next trading session and persist the adjustment to the position document with an advisory note. Add a contract test for this case using a synthetic calendar edit.

### EC-3 — Split / bonus ex-date falls between entry and present

- **Where:** `docs/requirements-spec.md` REQ-STOP / REQ-PLC; `docs/adr/0004-corporate-action-detection-threshold.md`.
- **Observation:** Open position spans an ex-date. ADR-0004 describes detection of the corporate action. No document states that the position's stop, add, and reduce levels are recomputed against the adjusted price series, nor what the position lifecycle does during the gap between detection and re-anchoring.
- **Recommendation:** Add REQ-PLC-00x: when corporate-action detection flags a non-terminal position, suspend RME monitoring for that position (new lifecycle state `SuspendedForCorporateAction` or reuse Suspended with a specific reason), emit an admin-actionable advisory via `rme_incidents` (LI-3), block new size additions, and require an admin or automated re-anchor pass before the position re-enters active monitoring. Recomputation rule: apply the adjustment ratio to the original entry, stop, and all level prices; write the adjustment to the position's audit history.

### EC-4 — Redis key eviction of a held trade-ledger lock ✅ RESOLVED 2026-04-20

- **Where:** `docs/requirements-spec.md` REQ-PORT-031; REQ-DATA-006a.
- **Observation:** The trade-ledger write lock lives in Redis. Under memory pressure Redis may evict the key, allowing a second writer to acquire the lock while the first is still executing. The result is concurrent writers on the same user's ledger — the exact condition REQ-PORT-031 exists to prevent.
- **Recommendation:** Add REQ-PORT-031b: implement the lock with Redlock semantics plus a fencing token (monotonically increasing per lock acquisition). Include the fencing token in every MongoDB update filter for the trade-ledger write — a write with a stale or missing token fails at the database layer. Configure the Redis instance hosting locks with `maxmemory-policy: noeviction` on the lock keyspace or a dedicated Redis database (`LOCK_DB_INDEX`) isolated from caches. Record the Redis topology expectation in `docs/system-architecture.md`.

### EC-5 — User revokes OAuth consent with the provider ✅ RESOLVED 2026-04-20

- **Where:** `docs/requirements-spec.md` REQ-AUTH.
- **Observation:** No session invalidation path is described beyond explicit sign-out. A user who revokes OAuth consent at the provider (Google/Microsoft/Facebook) will continue to hold a live session until their JWT naturally expires, because the platform never rechecks provider-side consent.
- **Recommendation:** Add REQ-AUTH-00x: on every OAuth token refresh attempt (done on a schedule or at a sensitive action), a failure caused by revoked consent or disabled account ends the session, flags the user `require_reauth`, propagates a WebSocket close to active connections, and surfaces a sign-in screen. Do not silently extend sessions past provider-side revocation.

### EC-6 — SQL Server transient outage during EODSR

- **Where:** `docs/requirements-spec.md` REQ-SIG / EODSR description.
- **Observation:** EODSR reads historical candles from SQL Server. A transient read error during the EOD window has no defined behaviour — partial-executing EODSR would be worse than not running at all, because it produces a half-universe of entry signals.
- **Recommendation:** Add REQ-SIG-00x: EODSR retries SQL Server reads with exponential backoff for up to `sys_config.eodsr.db_retry.max_seconds` (suggested default 120 seconds). On continued failure, mark the run `failed_for_review`, emit an admin Telegram + dashboard alert, and exit non-zero without emitting any entry signals for that day. The EODSR run is treated as atomic: either the full universe processes or nothing does.

### EC-7 — Notification Delivery Job processing queued notifications after the kill switch fires ✅ RESOLVED 2026-04-20

- **Where:** `docs/portfolio-risk-guidelines.md` § Operational Risk Controls; `docs/requirements-spec.md` § Notifications.
- **Observation:** Kill switch halts emission of new entry signals (UA-8) but in-flight but unsent notifications from earlier in the session are unaffected. This may be intentional (don't silence already-queued alerts) or unintended (admin expected a full notification freeze).
- **Recommendation:** Declare the policy explicitly in REQ-ADMIN-00x (the kill-switch scope requirement): the Notification Delivery Job continues delivering pre-existing queued notifications unless the admin separately drains the queue via a distinct admin action. Add an admin-UI control to drain the notification queue as a separate operation, with an audit trail entry.

### EC-8 — FYERS widget double-click / nonce reuse (UX side)

- **Where:** Phase 7 contract test 7 covers server-side nonce replay; no corresponding UX rule exists.
- **Observation:** User clicks the FYERS widget twice in quick succession. First click consumes the nonce; the second is silently refused by FYERS or the signed-payload server. From the user's perspective, nothing visible happens — which may be read as a platform bug and lead to a third click or a support ticket.
- **Recommendation:** Add REQ-ORDER-00x: the Phase 1 modal disables the action button immediately on first widget invocation and re-enables it only after the `finished` callback fires, an explicit cancel, or a widget-level timeout. On nonce-reuse rejection, surface a human-readable banner ("this order was already submitted — check FYERS for status"). Add a contract test for the double-click behaviour covering the UX state machine, not just the server nonce check.

### EC-9 — MongoDB replica-set failover during a LADS cycle ✅ RESOLVED 2026-04-20

- **Where:** `docs/requirements-spec.md` REQ-LADS / REQ-PORT; `docs/system-architecture.md` § Operational Database.
- **Observation:** A 10–30 second replica-set failover causes the LADS cycle in progress to abort. The next cycle runs in 15 minutes (`jobs.account_sync.intraday_interval_minutes` default). This is tolerable occasionally but a repeated miss is a real reconciliation gap.
- **Recommendation:** Add REQ-LADS-00x: log every aborted cycle as a counter metric `lads.cycle.aborted.total` with a reason label; if two consecutive cycles abort, emit an admin Telegram + dashboard alert; if three consecutive cycles abort, suspend new entry intent creation (REQ-ORDER-005 style block) until a successful LADS cycle completes. Document the expected failover behaviour in the MongoDB runbook.

### EC-10 — RME concurrency-retry exhaustion behaviour on the affected position ✅ RESOLVED 2026-04-20

- **Where:** `docs/adr/0003-rme-per-position-event-serialisation.md` REQ-RME-CONC-002 (3-attempt OCC retry).
- **Observation:** When OCC retries exhaust on a position, ADR-0003 § Decision flags it for admin review in `rme_events` (to be moved to `rme_incidents` per LI-3). It does not say whether the position continues to accept subsequent events — new LMDS stop checks, new LADS trade observations, new admin edits — or whether it is frozen until admin action. The wrong answer in either direction has a bad failure mode: frozen means silent misses on the affected position; open means compounding write conflicts.
- **Recommendation:** Adopt "frozen on exhausted-retry, with visibility" semantics. Add REQ-RME-CONC-00x: on retry exhaustion the position transitions to a `Suspended` lifecycle state with reason `concurrency_conflict_unresolved`, events continue to be enqueued on the channel (so no evidence is lost) but the RME skips their processing, the admin is notified, and admin resolution re-processes the queued events in order. Pair with a contract test and a runbook entry.

---

## Consolidated Open Questions

Of the original 15 Open Questions, five (Q1–Q5) were resolved by Critical Gap closures (see the 2026-04-18 review for audit trail). The remaining ten overlap substantially with items above and are consolidated here, labelled back to the originating section so the author can answer either the question or the recommendation.

- **Q-A (was Q6):** If the admin FYERS token refresh fails at market open, which subsystems halt and which continue? → See EC-1.
- **Q-B (was Q7):** Is CNC-only a hard product requirement, or is same-day exit support a Phase 10 consideration? → See LI-2; also informs whether REQ-NEXT-011 should be widened.
- **Q-C (was Q8):** Does the global kill switch halt LMDS stop monitoring, or only EODSR signal emission? → See UA-8; EC-7 extends the question to the notification queue.
- **Q-D (was Q9):** What idempotency key does the Notification Delivery Job use to prevent double-delivery across retries? → See UA-7.
- **Q-E (was Q10):** Is Azure App Configuration a hard startup dependency, or does the API warm-start from a cached last-known-good? → See UA-4.
- **Q-F (was Q11):** When the trading calendar changes, are existing positions' `time_stop_date` values recomputed? → See UA-2 and EC-2.
- **Q-G (was Q12):** Who owns the ADR-0004 corporate-action threshold calibration, against what sample, by what date? → See UA-10.
- **Q-H (was Q13):** What is the multi-tab policy — can the same user hold two active FYERS PLD streams, or does latest-tab win? → See UA-9.
- **Q-I (was Q14):** When RME concurrency retries exhaust, does the position continue to process subsequent events or is it frozen until admin action? → See EC-10.
- **Q-J (was Q15):** Is the 80% FYERS daily-budget projection a rolling 24-hour window, a calendar day, or something else? → See LI-4.

---

## Recommended triage order

1. **UA-1 (duplicate position prevention)** — highest silent-correctness risk; partial unique index is a one-line MongoDB change with a large correctness payoff.
2. **EC-4 (Redis lock eviction)** — a latent concurrency bug in the critical-path write; Redlock + fencing token is the standard fix.
3. **LI-3 (`rme_events` vs. incidents)** — schema decision that gets harder to unwind the more code writes to `rme_events`.
4. **LI-5 (lazy channel creation)** — trivial code change; prevents a meaningless channel churn in the synchronously-rejected path.
5. **UA-4 (App Config startup failure)** — a pragmatic hazard that will bite in a real Azure outage; relatively cheap to spec and implement.
6. **UA-2 + EC-2 (trading calendar recompute)** — coupled pair; best solved together in a single requirement.
7. **UA-9 (multi-tab session)** — UX-visible and affects FYERS token usage.
8. **EC-1 (admin token mid-session failure)** — user-visible degradation without indicator.
9. **UA-8 + EC-7 (kill-switch scope)** — operational clarity before it is needed in anger.
10. **Remaining items** — LI-1, LI-2, LI-4, UA-3, UA-5, UA-6, UA-7, UA-10, EC-3, EC-5, EC-6, EC-8, EC-9, EC-10 — most are small, self-contained spec additions and can be batched.

---

## Cross-reference table — which canonical docs are most affected

| Doc | Items that touch it |
| --- | --- |
| `docs/requirements-spec.md` | LI-1, LI-2, LI-4, UA-1, UA-2, UA-3, UA-5, UA-6, UA-7, UA-8, UA-9, EC-1, EC-3, EC-4, EC-5, EC-6, EC-8, EC-9, EC-10 |
| `docs/system-config.md` | LI-4, UA-4, UA-6, UA-9, EC-6 |
| `docs/system-architecture.md` | UA-4, UA-9, EC-1, EC-3, EC-4, EC-9 |
| `docs/implementation-roadmap.md` | LI-2, UA-2, UA-10 |
| `docs/portfolio-risk-guidelines.md` | LI-1, UA-2, UA-8, EC-7 |
| `docs/data-management.md` | LI-3, UA-6 |
| `docs/adr/0002` | UA-4 |
| `docs/adr/0003` | LI-3, LI-5, EC-10 |
| `docs/adr/0004` | UA-10 |
| `docs/operations/fyers-api-budget.md` | LI-4 |
| Runbooks | EC-1, EC-4, EC-9 |

---

*End of open-items review. When continued in a new conversation, the recommended workflow is unchanged: verify each finding against the current state of the canonical docs, propose options with alternatives considered, await approval, execute the edits, and mark the item RESOLVED in place with the original observation retained for audit.*
