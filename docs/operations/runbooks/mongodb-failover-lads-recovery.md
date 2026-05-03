# MongoDB Replica-Set Failover / LADS Recovery

**Phase required:** A
**Last reviewed:** *(not yet reviewed — stub)*
**Owner:** Platform admin

## When to use

Open this runbook when any of these occur:

- The admin System Health widget (REQ-ADMIN-014) shows a LADS health warning with the consecutive-abort count at 2 or higher and the reason labelled `mongo_write_error` or `mongo_connection_lost`.
- An admin Telegram alert and dashboard alert fire indicating sustained LADS failure — three or more consecutive LADS cycles aborted for MongoDB reasons — with the alert body naming the consecutive abort count and the elapsed time since the last successful cycle.
- A `lads_sustained_failure` incident record appears in `rme_incidents` with `incident_type: lads_sustained_failure`.
- Worker OTLP logs show repeated `MongoConnectionException`, `MongoWriteException`, or `retryWrites` exhaustion across multiple LADS cycles.
- Reports from multiple users that the portal shows stale portfolio data and manual sync attempts fail with a generic "sync in progress" timeout.
- New entry signal creation is blocked platform-wide (see REQ-PORT-021a(c) — the platform automatically suspends new entry intent creation on 3 consecutive LADS aborts), and users report that the entry advisory "Place Entry" action button is greyed out.

## What happens during a MongoDB replica-set failover

MongoDB replica-set failovers are brief (typically 10–30 seconds per REQ-PORT-021a) and self-healing. The following platform behaviours are expected during the failover window:

- **LADS cycles abort.** The LADS poll loop attempts to read user holdings and trades from MongoDB. With no primary elected, writes fail and reads may also fail or time out. The cycle aborts and increments `lads.cycle.aborted.total` with `reason: mongo_connection_lost` or `mongo_write_error`.
- **retryWrites in flight.** Any MongoDB write operation using the driver's built-in `retryWrites: true` (the default for replica-set connections) retries automatically on the server side. If the failover duration exceeds the driver's internal retry budget, the write surfaces as a `MongoException` to the caller. This is expected and safe — the driver does not silently drop writes.
- **RME channel consumer OCC retries may exhaust.** If an RME channel consumer write attempt occurs during the failover window and the driver-level retry budget is exhausted, the consumer treats the failure as a third-consecutive-abort equivalent per REQ-PORT-021a, which triggers an admin advisory. This does NOT mean the position enters a frozen state — it means the RME write attempt during the failover window surfaced as a failure to the application layer. On the next successful cycle, normal processing resumes.
- **New entry intent creation is suspended.** The platform automatically blocks new `intent_ledger` entries for all users once the consecutive-abort count reaches 3. This suspension is released automatically on the next successful LADS cycle — no manual intervention is required for the auto-release path.
- **Portal users see stale data.** The dashboard, holdings, and positions pages continue to show the last successfully synced portfolio snapshot. Users attempting a manual sync see a timeout or "unavailable" message. This is expected during the failover window.
- **No data loss.** Writes that were acknowledged before the failover are durable and present after the new primary is elected. Writes in-flight during the failover that were not acknowledged are retried by the driver or re-issued by the application on the next cycle. The automatic suspension of entry intent creation prevents new orders from being initiated during the window when the platform cannot confirm portfolio state.

## Preconditions

Before starting, verify:

1. Admin is signed into the portal with step-up re-authentication still valid (REQ-SEC-011).
2. The MongoDB replica-set deployment is known — check whether it is an Atlas cluster or a self-managed replica set. The deployment type determines the failover monitoring surface (Atlas console vs. direct replica-set monitoring).
3. A `lads_sustained_failure` incident record exists in `rme_incidents`, or the consecutive-abort count on the System Health widget confirms entry intent suspension is active. If neither is true, this runbook may not be needed — monitor briefly and confirm the failover has self-resolved.
4. The Worker Service is running. If the Worker has also crashed or entered a crash-loop during the failover, resolve that first — restart the Worker before proceeding, as LADS recovery depends on the Worker being active.

## Steps

### Step 1 — Confirm the failover event

Identify the MongoDB failover event. The approach depends on your deployment type:

**If using MongoDB Atlas:**
1. Navigate to the Atlas console → your cluster → **Metrics** tab.
2. Look for a **Primary Election** event in the timeline. Atlas records each election as an event with a timestamp.
3. Check the **Connections** and **Operation Execution Time** charts for a corresponding drop or spike indicating a brief outage window.
4. Confirm the most recent primary election completed within the last `N` minutes, where `N` is the elapsed time since the first LADS consecutive abort was recorded.

**If using a self-managed replica set:**
1. Connect to a secondary node via `mongosh` and run `rs.status()`.
2. Look for `stateStr: "PRIMARY"` on one of the nodes and verify the `electionDate` field for the most recent election.
3. Run `rs.printReplicationInfo()` and check `logLength` for any unusual backlog that might indicate a prolonged sync gap.

**Regardless of deployment:**
- Note the failover start and end timestamps. The failover window is typically 10–30 seconds; anything consistently over 60 seconds should be escalated to the infrastructure team.
- Check the Worker's OTLP logs for `MongoConnectionException` or `MongoWriteException` entries whose timestamps align with the failover window. This confirms the platform-side impact.

### Step 2 — Assess the abort count and incident state

Navigate to **Admin → System Health** and examine the LADS section. Note:

- **Consecutive abort count.** The number shown on the System Health widget. A value of 3 or higher means entry intent suspension is active.
- **Elapsed time since last successful cycle.** If more than 5 minutes have elapsed since the last successful LADS cycle, the failover may have uncovered a deeper issue (e.g., the new primary is not accepting writes or the application's MongoDB connection string has not updated).
- **`lads_sustained_failure` incident status.** Navigate to **Admin → Risk → RME Incidents** and check for a `lads_sustained_failure` record. Note its `status` and `created_at`.
- **Entry intent suspension status.** The System Health widget should show a clear indicator: "Entry intents suspended — sustained LADS failure" or similar. Also confirm whether any users have reported blocked entry actions (greyed-out "Place Entry" button).

### Step 3 — Verify replica-set primary election is complete

Before the platform auto-recovers, confirm the replica set is stable:

**Atlas:**
1. In the Atlas console, verify the cluster's **Primary** status is stable — no recent or in-progress election events.
2. Check the cluster **Overview** panel. The primary node should be clearly identified and the cluster state should be green.
3. Confirm the oplog window is within normal bounds (typically a few hours on Atlas; check your cluster's specific oplog retention).

**Self-managed:**
1. Connect via `mongosh` and run `rs.status()`.
2. Verify `stateStr: "PRIMARY"` is set on exactly one node and `stateStr: "SECONDARY"` on the remaining voting nodes.
3. Verify `health: 1` for all nodes in the set.
4. Verify `lastHeartbeatMessage` does not indicate any error or stale state on any node.

**Readiness check — can the application write?**
Perform a test write to confirm the primary is accepting writes:
1. Connect via `mongosh` to the primary.
2. Run a test write: `db.health_check.insertOne({ check: "lads_recovery", timestamp: new Date() })`.
3. Confirm the write succeeds and the document appears on read-back.
4. Clean up: `db.health_check.deleteOne({ check: "lads_recovery" })`.

### Step 4 — Monitor the auto-recovery path

The platform is designed to self-recover from a LADS sustained-failure incident once MongoDB connectivity is restored. Do not manually intervene yet — instead, observe the auto-recovery:

1. From **Admin → System Health**, watch the LADS consecutive-abort counter. After the next successful LADS cycle, this counter should reset to 0.
2. Confirm the `lads_sustained_failure` incident transitions from `open` to `resolved` automatically within 1–2 LADS cycle intervals.
3. Confirm the entry intent suspension clears — the "Entry intents suspended" indicator on the System Health widget should disappear.
4. Confirm that LADS cycle timestamps in the System Health widget show recent successful completion.

**Expected auto-recovery timeline:**
- Failover ends: T+0s
- Next LADS cycle starts: T+0s to T+60s (depends on `jobs.account_sync.intraday_interval_minutes` — 5 minutes at default, and per-user staggering)
- First successful post-failover cycle completes, abort counter resets: T+30s to T+90s
- Entry intent suspension clears: automatic on abort counter reset
- `lads_sustained_failure` incident resolved: automatic on abort counter reset

### Step 5 — If auto-recovery does not trigger

If two consecutive LADS cycle intervals pass after the replica set is confirmed stable (Step 3) and the abort counter has not reset to 0, proceed with these manual checks:

**Check 1 — Is the Worker still running?**
Navigate to **Admin → System Health → Worker** and confirm the Worker shows `status: healthy`. If the Worker is not running, restart it from the Azure portal (App Service → your Worker app → **Restart**). If the Worker is in a crash-loop, check the Worker logs for startup errors — a failed Redis singleton lease or a missing `sys_config` sentinel row are common causes.

**Check 2 — Is the LADS poll loop still active?**
Check the Worker's OTLP logs for the `LADS` source. Look for lines containing `lads_cycle_starting` or `lads_cycle_aborted`. If no LADS log lines are present in the last 5 minutes, the LADS poll loop may have crashed silently — restart the Worker.

**Check 3 — Is the MongoDB connection string correct after failover?**
In an Atlas deployment, the connection string does not change after a failover (Atlas DNS handles routing). In a self-managed deployment, confirm that the application's connection string includes all replica-set members and that the newly-elected primary is reachable from the Worker's network. App Configuration (`REQ-CONFIG-004`) is the authoritative source for connection strings — verify the current value in Azure App Configuration or the bootstrap environment.

**Check 4 — Force a LADS cycle trigger.**
Navigate to **Admin → Jobs → Account Sync** and click **Trigger LADS Cycle** (or equivalent manual trigger). This forces an immediate LADS poll cycle across all users, bypassing the scheduled interval. If this trigger fails with a MongoDB error even after the replica set is confirmed healthy, escalate to engineering.

**Check 5 — If all else fails, restart the Worker.**
From the Azure portal, restart the Worker App Service. On restart, the channel registry is rebuilt from MongoDB, LADS and LMDS poll loops re-initialise, and the consecutive-abort counter starts from 0. After restart, confirm the first two LADS cycles complete successfully.

### Step 6 — Verify end-to-end recovery

Once auto-recovery or manual recovery has completed:

1. **System Health widget:** Confirm all LADS indicators are green:
   - Consecutive abort count: 0.
   - Last successful cycle: timestamp within the expected interval.
   - Entry intent suspension: cleared.
   - `lads_sustained_failure` incident: resolved with `resolved_at` set.

2. **RME Incidents:** Navigate to **Admin → Risk → RME Incidents** and confirm the `lads_sustained_failure` record has `status: resolved`.

3. **Portal data freshness:** Log in as a test user (or use the admin portal's per-user view) and confirm the dashboard shows recent portfolio data with a successful sync timestamp after the failover window.

4. **Entry intent creation:** Navigate to a chart page where an entry advisory is active and confirm the "Place Entry" button is no longer greyed out with the LADS-failure suspension message. The Phase 1 modal should open normally.

5. **OTLP traces:** Check the Worker's OTLP trace stream for the LADS source. Confirm the absence of `lads_cycle_aborted` events with `reason: mongo_*` in the most recent 3 cycles.

6. **Notification feed:** Confirm no lingering LADS-failure admin alerts remain in the notification feed. If any remain, dismiss them manually from **Admin → Notifications**.

## Rollback

**If auto-recovery does not trigger within the expected timeline**, Step 5 provides escalation steps ending with a Worker restart. A Worker restart is the single rollback action available — it re-initialises all poll loops from a clean state. No MongoDB-level rollback is needed because the platform's design (entry intent suspension, retryWrites, idempotent sync) prevents data corruption during a failover.

**If manual intervention causes unexpected behaviour** (e.g., after a forced LADS cycle the consecutive-abort counter does not reset):
- Do not force additional LADS cycles. Wait for the scheduled cycle to run.
- If the scheduled cycle also does not clear the abort counter, restart the Worker.

**If a user's portfolio data appears incomplete after recovery** (some trades or holdings not reflected):
- Wait for the next scheduled EOD sync before triggering any manual sync. EOD sync has the broadest data coverage and will close any gaps left by interrupted intraday cycles.
- If EOD sync does not resolve the gap, trigger a manual sync for the affected user from **Admin → Users → [user] → Trigger Account Sync**.

## Post-incident

- Record the incident outcome in `audit_events`. The portal records the `lads_sustained_failure` incident resolution automatically. Note any manual steps taken (Worker restart, forced LADS cycle, connection string checks).
- If the failover was planned (e.g., a MongoDB maintenance operation), confirm the maintenance window notification was sent to users in advance per the platform's standard communication policy.
- If the failover was unplanned, escalate to the infrastructure team to investigate root cause — replica-set failovers should not occur without a triggering event (network partition, primary node failure, or maintenance operation).
- Review the failover duration. If the replica-set election took longer than 60 seconds, the MongoDB deployment may need tuning (e.g., priority settings, election timeout configuration, or a larger instance tier).
- If multiple failovers occur within a rolling 7-day window, escalate — this indicates a systemic stability issue with the MongoDB deployment.
- Update the **Last reviewed** date at the top of this runbook.

## Notes

- MongoDB replica-set failovers are expected to be brief (10–30 seconds) and self-healing. The three-consecutive-abort threshold in REQ-PORT-021a allows for a single brief failover to self-resolve without triggering platform-wide suspension. Only failovers that produce 3+ consecutive aborted cycles — either from repeated failovers or a prolonged outage — activate the suspension.
- The platform's auto-recovery path is the primary resolution mechanism. Manual intervention (Step 5) is the exception, not the default. Wait for at least one full LADS cycle interval after the replica-set is confirmed stable before proceeding with manual steps.
- The entry intent suspension during a sustained LADS failure is by design — it prevents users from initiating orders through the platform when the portfolio state cannot be reliably confirmed. The suspension is automatically released and does not require manual clearance. Do not circumvent the suspension by editing `rme_incidents` or database state directly.
- During Phase A (single-instance Worker), RME channel consumer OCC retries that exhaust during a failover window will produce admin advisories but will not cause position freezes (the freeze requires 4 consecutive OCC failures on the same event, not a single driver-level retry exhaustion). See `rme-concurrency-freeze-resolution.md` if a position does enter frozen state concurrent with a failover event.
- The `retryWrites: true` MongoDB driver setting (default for replica-set connections) is the first line of defence against transient failover write failures. If you observe repeated retryWrites exhaustion in the Worker logs outside of failover windows, investigate MongoDB connection pool configuration and network latency before adjusting the three-consecutive-abort threshold.
