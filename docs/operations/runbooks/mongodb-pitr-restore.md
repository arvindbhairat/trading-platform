# Runbook 10 — MongoDB Restore from Point-in-Time (PITR)

**REQ coverage:** REQ-BCP-001, REQ-BCP-006  
**Last reviewed:** 2026-05-05 — drill-execution-001 (P8-T8); field-test required before Phase A user onboarding

---

## When to use

Open this runbook when:
- MongoDB Atlas emits a cluster health alert indicating data loss or corruption.
- The `trade_ledger`, `positions`, `equity_curve`, `audit_events`, or `portfolio_snapshots` collections contain incorrect or missing documents that cannot be repaired by the standard reconciliation flow.
- A restore drill exercise is being conducted per REQ-BCP-007.
- An admin confirms accidental deletion of documents that must be recovered.

Do **not** use this runbook for individual-document correction that can be done via the admin portal manual adjustment flow.

---

## Preconditions

- MongoDB Atlas PITR is enabled on the cluster (continuous backup, 48h restore window).
- You have Atlas project owner or organization owner access to the Atlas control plane.
- The API and Worker services are shut down (see Step 1) to prevent in-flight writes during restore.
- You have identified the target point-in-time (UTC timestamp) to restore to.
- A separate restore cluster has been provisioned or you are prepared to provision one.

---

## Steps

### 1 — Shut down API and Worker to freeze writes

1. In the Azure portal, navigate to the API App Service and stop the app.
2. Navigate to the Worker App Service and stop the app.
3. Confirm no active WebSocket connections remain (check OpenTelemetry `connections.active` metric — should be 0 within 30 seconds of stopping).
4. Record the shutdown time (UTC) in the incident log.

### 2 — Identify the restore target timestamp

1. Open the Atlas UI → Clusters → your cluster → Backup.
2. Under "Point-in-Time Restore," note the earliest and latest available restore timestamps.
3. Choose the target timestamp:
   - It must be **before** the data loss event.
   - It must be **within the 48-hour restore window**.
4. Record the chosen timestamp in the incident log.

### 3 — Restore to a new cluster (preferred) or in-place

**Preferred path: restore to a new cluster**

1. In Atlas, click "Restore" → "Point-in-Time" and enter the target timestamp.
2. Select "Restore to a new cluster" (do NOT restore over the live cluster initially).
3. Name the new cluster `<cluster-name>-restore-<YYYYMMDD>`.
4. Wait for the restore to complete (typically 20–60 minutes depending on data volume).
5. Once complete, validate the restored cluster (Step 4) before cutting over.

**In-place restore (only if data loss is confirmed and there is no risk of overwriting good data):**

1. In Atlas, click "Restore" → "Point-in-Time."
2. Select "Restore to existing cluster" and choose the live cluster.
3. Acknowledge that this overwrites all data written after the restore point.
4. Wait for restore to complete.
5. Proceed directly to Step 4.

### 4 — Validate the restored dataset

Run the following checks against the restored cluster before cutting the application over:

1. **Document count check:** Compare document counts across all critical collections (`trade_ledger`, `positions`, `equity_curve`) against the last known good snapshot in `portfolio_snapshots`.
2. **Referential integrity:** Spot-check that `positions` entries referenced in `trade_ledger` records exist.
3. **Audit trail:** Confirm `audit_events` records exist up to the restore timestamp.
4. **Index check:** Run `db.collection.getIndexes()` on each critical collection and confirm indexes match the expected schema from `docs/data-management.md`.
5. **TTL expiry check:** Confirm `sessions` and `notifications` TTL indexes are present and active.

Record all validation results in the incident log.

### 5 — Update application connection string

If restoring to a new cluster:

1. In the Azure portal → Key Vault → Secrets, update the `MongoDbConnectionString` secret to point to the restored cluster's connection string.
2. Confirm the new secret version is active (not disabled or expired).

If restoring in-place:
- No connection string change is required.

### 6 — Restart API and Worker

1. In the Azure portal, start the API App Service.
2. Confirm the startup health check returns 200 on `/api/v1/healthz` within 30 seconds.
3. Start the Worker App Service.
4. Confirm the Worker heartbeat log appears within 60 seconds.
5. Monitor OpenTelemetry traces for MongoDB connection errors for 5 minutes post-restart.

### 7 — Run reconciliation

1. Trigger a manual "Rebuild trade ledger from broker" for each affected user via the admin portal (REQ-PORT-027).
2. Verify that `ledger_snapshot_version` increments correctly after the rebuild.
3. Confirm the `equity_curve` and `portfolio_snapshots` reflect updated state.

---

## Verification

- `/api/v1/healthz` returns 200.
- Worker heartbeat log line appears every configured interval.
- No MongoDB connection errors in OTLP traces for 10 minutes.
- `audit_events` contains a record of this restore operation (written by admin via the portal after the incident is resolved).

---

## Rollback

If the restored cluster is invalid or the cutover causes application errors:

1. Stop the API and Worker.
2. Revert the Key Vault `MongoDbConnectionString` secret to the previous version (Atlas live cluster).
3. Restart API and Worker against the original cluster.
4. Record the rollback decision and timestamp in the incident log.
5. Open a Priority 1 follow-up ticket to root-cause the restore failure.

---

## Post-incident

1. Write an `audit_events` record:
   - `event_type`: `bcp_restore_mongodb_pitr`
   - `restored_to_timestamp`: (UTC)
   - `outcome`: `success` or `failure`
   - `operator`: admin identity
   - `notes`: free text summary
2. If this was a restore drill, record the outcome in `audit_events` with `event_type: bcp_drill_mongodb`.
3. Decommission the temporary restore cluster after validation is complete.
4. If any step failed, open a Priority 1 follow-up and link it to the `audit_events` record.
5. Update this runbook if any steps were incorrect or missing.

---

**Last reviewed:** 2026-05-05 — drill-execution-001 (P8-T8); field-test required before Phase A user onboarding
