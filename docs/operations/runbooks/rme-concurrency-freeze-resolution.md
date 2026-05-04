# RME Concurrency-Freeze Resolution

**Phase required:** A
**Last reviewed:** 2026-05-05 — finalized in P8-T7 (Phase A mandatory runbook refinement)
**Owner:** Platform admin

## When to use

Open this runbook when any of these occur:

- The admin System Health widget (REQ-ADMIN-014) shows the `rme.channel.max_backlog_depth` metric in a warning or critical state alongside one or more positions suspended with reason `concurrency_conflict_unresolved`.
- An admin-only `position_concurrency_alert` notification is received via Telegram or in the portal notification feed.
- OTLP trace inspection shows repeated OCC version-conflict failures (`_version` mismatch) from the RME channel consumer for the same position, with no resolution across multiple Worker cycles.
- The admin RME Incidents page shows one or more `rme_incidents` records with `incident_type: concurrency_conflict_unresolved` and `status: open`.

## Preconditions

Before starting, verify:

1. Admin is signed into the portal with step-up re-authentication still valid (REQ-SEC-011).
2. No other admin is currently working the same incident. Only one admin exists per REQ-ROLE-001, so this is usually trivially true, but in a Phase B transition or admin transfer scenario, confirm with any peers.
3. The Worker Service is running and healthy. If the Worker is in a crash-loop or is stopped, resolve that first before proceeding (the Worker must process backlogged events on resume).
4. The affected position's MongoDB document is accessible and the `rme_incidents` collection is reachable.
5. A recent (within the last 10 minutes) backup or snapshot of the `positions` and `rme_events` collections is available for forensic review, if needed. MongoDB point-in-time restore capability should be verified before making any changes to position documents.

## Steps

### Step 1 — Identify the frozen position and confirm the incident

Navigate to **Admin → Risk → RME Incidents**. The page lists all `rme_incidents` records with `status: open`, sorted by `created_at` descending. Identify the incident(s) matching:

- `incident_type`: `concurrency_conflict_unresolved`
- `detail.retry_count`: 4 (indicating OCC retry exhaustion per REQ-RME-CONC-002)
- `detail.last_conflicting_version`: a non-null value

For each incident, note:
- The `position_id` and `user_id`.
- The `detail.event_type` (what RME event caused the exhaustion).
- The `detail.last_conflicting_version` value — the `_version` the position document was at when the last retry conflict was observed.

### Step 2 — Verify position document consistency in MongoDB

Before clearing any incident, you must verify that the suspended position's MongoDB document is internally consistent. Navigate to the position details from **Admin → Risk → Positions** or query MongoDB directly via `mongosh`.

Verify the following invariants hold:

**Check 1 — Document structure integrity.** The position document must have all required fields per the `positions` schema in `docs/data-management.md` and must not have null or corrupt values in any of: `_id`, `_version`, `user_id`, `symbol`, `status`, `lifecycle_state`, `entry_price`, `quantity`, `current_stop_level`, `rme_profile_snapshot`. If any required field is null or has an unexpected type, stop and escalate — this indicates a deeper data integrity issue that must be investigated before proceeding.

**Check 2 — `_version` field is a positive integer.** The `_version` field must be a long integer >= 1. A value of `0` indicates the document was created before the OCC mechanism was introduced and has never been written by the RME — this itself is a finding that should be noted in the incident record, but it does not block resolution.

**Check 3 — Lifecycle state is `Suspended` with reason `concurrency_conflict_unresolved`.** Confirm the document's `suspended_reason` (or equivalent field) carries the exact value `concurrency_conflict_unresolved`. If the reason is anything else (e.g., `circuit_limit_breach`, `kill_switch_activated`), the position was suspended by a different mechanism — do not treat this as an OCC-freeze incident. Close this runbook and open the appropriate runbook for the actual suspension reason.

**Check 4 — No overlapping or corrupt event chain in `rme_events`.** Query `rme_events` for the position with `{ position_id: <id> }`, ordered by `event_at` ascending. Verify:
- Events are sequential and non-overlapping in time.
- The last event before the `Suspended` transition is an OCC version-conflict event matching `concurrency_conflict_unresolved`.
- No event after the `Suspended` timestamp should have a later event_at than the incident `created_at` — the RME channel consumer stops processing events for frozen positions per REQ-RME-CONC-007(c). If events after the freeze timestamp exist, the channel invariant has been violated — escalate to engineering.

**Check 5 — FIFO-derived state and position state consistency (cross-check).** The position's `average_buy_price`, `quantity`, and `realised_pnl` (if applicable) should be consistent with the user's `trade_ledger` at the time the position entered `Open` state. If the position is in `PendingEntry`, this check is not applicable. For `Open` positions, verify that no subsequent trade-ledger update has invalidated the position's cost basis. If a discrepancy is found, note it in the incident record — the freeze release will re-enable the position, and the next RME processing cycle will re-evaluate but will not automatically correct a stale cost basis.

### Step 3 — Classify the root cause

From the evidence gathered in Steps 1 and 2, classify the freeze:

**Class A — Transient OCC exhaustion (expected behaviour).** The position document shows no structural corruption, the `_version` chain is intact, and the freeze occurred during a brief period of high-frequency events (e.g., rapid LMDS price updates arriving faster than the channel consumer could process and commit). The position was correctly suspended by the OCC safety net. This is the normal and expected operating mode of REQ-RME-CONC-007. Resolution: proceed to Step 4.

**Class B — Intermittent write-path failure.** The freeze was preceded by MongoDB write-timeouts, retryWrites exhaustion, or Redis lock eviction logs in the Worker's OTLP traces. The position document itself is consistent, but the Worker's write path to MongoDB was degraded. Resolution: proceed to Step 4, but after resolution, investigate and address the underlying write-path issue. Check Worker OTLP traces for `MongoWriteException`, `MongoConnectionException`, or `retryWrites` exhaustion events.

**Class C — Data corruption or structural violation.** The position document fails one or more of the consistency checks in Step 2. DO NOT proceed with Step 4. Escalate to engineering for forensic analysis. If the position document can be repaired, the repair must be performed as an audited MongoDB operation with a documented change record in `audit_events`. Only after the document is repaired may this runbook proceed.

**Class D — Channel invariant violation.** Events exist in the position's `rme_events` with `event_at` timestamps after the freeze timestamp (Check 4 violation). This indicates the per-position channel serialisation invariant has been broken. Escalate immediately — this is a correctness bug that requires code-level investigation and a fix before any freeze release.

### Step 4 — Clear the incident via admin portal

For Class A or Class B freezes only:

1. Navigate to **Admin → Risk → RME Incidents → [incident ID]**.
2. Review the incident detail panel showing the position ID, event type, retry count, and last conflicting version.
3. Click **Resolve Incident**. The portal presents a confirmation dialog with a mandatory text field for the resolution note.
4. Enter a resolution note describing the findings from Steps 2 and 3. Include:
   - The classification (Class A or Class B).
   - A summary of the consistency checks performed and their outcomes.
   - Any relevant telemetry observations (e.g., "Worker OTLP traces show no concurrent writes, confirmed Class A").
5. Confirm.

On confirmation:

- The `rme_incidents` record transitions to `status: resolved` with `resolved_at` and `resolved_by` set.
- The position document is updated: `lifecycle_state` transitions from `Suspended` to its prior state (`Open` or `PendingEntry`), and the `suspended_reason` field is cleared.
- The RME channel consumer resumes processing enqueued events in FIFO order for this position. Events that accumulated during the freeze are not dropped — they are processed in sequence.

### Step 5 — Verify channel resumption

Immediately after clearing the incident:

1. Navigate to **Admin → System Health** and observe the RME section. Confirm `rme.channel.max_backlog_depth` for the affected position decreases over the next 30–60 seconds as the channel consumer drains the backlog.
2. Query `rme_events` for the position with `{ position_id: <id> }` ordered by `event_at` descending. Confirm new events appear with `event_at` timestamps after the incident resolution time.
3. Verify the position's lifecycle state is no longer `Suspended` by checking the position detail page under **Admin → Risk → Positions** or by querying the `positions` collection directly.
4. If the position is `Open`, verify that stop-level monitoring has resumed by checking the `current_stop_level` field on the position document is populated and non-null.
5. Confirm the `position_concurrency_alert` admin notification has cleared. If it remains visible in the notification feed, dismiss it manually from **Admin → Notifications**.

### Step 6 — Monitor for recurrence

For the next 30 minutes after resolution:

1. Keep the `rme.channel.max_backlog_depth` metric and the `rme.channel.idle_suspended_count` gauge visible on the admin System Health widget.
2. Watch for a new `concurrency_conflict_unresolved` incident on the same position. If the same position re-freezes within 30 minutes, stop and escalate — a single position experiencing repeated OCC exhaustion suggests a systemic issue (e.g., an unexpectedly high-frequency event producer, a slow channel consumer, or a document-size growth problem) that cannot be resolved by repeated incident clearance alone.
3. If a different position freezes, treat it as independent — repeat this runbook for the new incident.

## Rollback

**After incident clearance (Step 4 completed):** If clearing the incident causes unexpected behaviour — e.g., the position transitions to an invalid state, the channel consumer begins discarding events, or the position immediately re-freezes — the admin may re-suspend the position manually from **Admin → Risk → Positions → [position] → Suspend Position** with reason `manual_admin_suspension`. This pauses processing and contains the issue while engineering investigates.

**Before incident clearance:** If at any point during Steps 1–3 the admin suspects this is not a clean OCC freeze, do not proceed to Step 4. Instead, escalate to engineering. The frozen state is safe by design — events are queued but not lost, and the position is not trading until released.

## Post-incident

- Record the incident outcome in `audit_events`. The portal does this automatically on incident clearance (Step 4), but note any manual MongoDB queries or interventions performed outside the portal.
- If the freeze was Class B (intermittent write-path failure), open a follow-up item to investigate the underlying MongoDB write-path issue. Check OTLP traces for patterns in `retryWrites` exhaustion events, replica-set failover timing, or network latency to the MongoDB deployment.
- If the freeze was Class A but occurred during a period of normal market activity with no unusual conditions, consider whether the `_version` OCC retry count (3 retries per REQ-RME-CONC-002) is appropriate for the current event frequency. Changing this threshold requires updating `sys_config` and the corresponding requirement and ADR — do not change it without a documented review.
- If freeze incidents recur across multiple positions on the same trading day, escalate — this may indicate a systemic issue such as a MongoDB replica-set failover (see `mongodb-failover-lads-recovery.md` runbook) or an unexpected event-volume spike from a misconfigured LMDS poll interval.
- Update the **Last reviewed** date at the top of this runbook.

## Notes

- The frozen-with-visibility mechanism (REQ-RME-CONC-007) is a safety net, not a failure state. It exists because silent data corruption from concurrent OCC writes is worse than a suspended position. Treat freeze incidents as evidence that the safety net is working correctly.
- The `_version` OCC mechanism is designed to catch crash-recovery races and unforeseen concurrent write scenarios. In normal single-instance Worker operation, the per-position channel serialisation (REQ-RME-CONC-001) prevents concurrent writes from occurring in the first place. A freeze in single-instance mode may indicate a bug in the channel serialisation — escalate if it occurs outside of a Worker restart or crash scenario.
- When the Worker has restarted (deployment or crash), brief OCC conflicts are expected as the channel registry is rebuilt and event producers spin up. A single freeze on restart is not concerning. Multiple freezes within 5 minutes of restart should be escalated.
- The `rme_incidents` collection uses a TTL index that expires resolved incidents 6 months after `resolved_at`. If you need to preserve a specific incident record for a longer audit window, note the `_id` and export it before the TTL fires.
