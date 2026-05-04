# Runbook 12 — SQL Server Restore (Backtest Database)

**REQ coverage:** REQ-BCP-003, REQ-BCP-006  
**Last reviewed:** 2026-05-05 — drill-execution-001 (P8-T8); field-test required before Phase A user onboarding

---

## When to use

Open this runbook when:
- The `backtest` SQL Server database is corrupted, missing, or contains incorrect results that cannot be repaired by re-running the backtest engine.
- A restore drill exercise is being conducted per REQ-BCP-007.

**Note:** Per REQ-BCP-003, stored backtest results are reproducible from retained market data, Signal code, and Signal Subscription parameter history. Before committing to an 8-hour RTO restore, evaluate whether re-running affected backtests is faster.

---

## Preconditions

- You have contributor or owner access to the Azure SQL Server resource group.
- The API service is shut down if the database will be restored in-place.
- You have identified the target point-in-time (UTC) within the 7-day PITR retention window, or a weekly backup from the long-term retention policy.
- Backtest re-run has been evaluated and ruled out (or is insufficient because the Signal code or parameters have been deleted).

---

## Steps

### 1 — Assess whether restore is necessary

Answer the following questions before opening a full restore:

1. Are the affected backtest result records identifiable by `signal_run_id` or `entry_signal_id`?
2. Is the source Signal code still available in the repository and the Signal Subscription parameters in MongoDB?
3. Can the backtests be re-run against the existing market data to regenerate results?

If yes to all three → **re-run the backtest** rather than restoring the database. Record the re-run decision and proceed to the verification step.

If no → continue with the restore procedure below.

### 2 — Shut down dependent API services

1. Stop the API App Service in the Azure portal.
2. Confirm no active backtest jobs are running (check `signal_runs` in MongoDB for `status: running` entries).

### 3 — Identify the restore target

**Option A — PITR restore (within 7-day window)**

1. Azure portal → SQL Server → `backtest` database → Restore.
2. Choose "Point-in-time" restore.
3. Enter the target UTC timestamp.

**Option B — Long-term retention restore (weekly backup)**

1. Azure portal → SQL Server → `backtest` database → Manage Backups.
2. Select the applicable weekly backup.
3. Initiate restore.

### 4 — Perform the restore

1. In the Azure portal, click "Restore."
2. Select "New database" for the restore target: `backtest-restore-<YYYYMMDD>`.
3. Confirm restore region is Central India per REQ-LEGAL-009.
4. Initiate restore and wait for completion (15–45 minutes for PITR; up to 2 hours for LTR from weekly backup).

### 5 — Validate the restored database

1. **Schema check:** Confirm the `signal_runs` result table structure matches the current FluentMigrator migration state.
2. **Row count check:** Query the result count for at least 3 known `signal_run_id` values and confirm the row counts match the expected state from the pre-incident MongoDB `signal_runs` records.
3. **Referential integrity:** Spot-check that `signal_run_id` values in the backtest database match `signal_runs._id` values in MongoDB.
4. **DBCC check:** Run `DBCC CHECKDB ('backtest-restore-<YYYYMMDD>')` and confirm no errors.

### 6 — Update the connection string

1. In Key Vault, update the `SqlBacktestConnectionString` secret to point to the restored database.
2. Confirm the new secret version is active.

### 7 — Restart API

1. Start the API App Service.
2. Confirm `/api/v1/healthz` returns 200.
3. Trigger a test backtest run via the Signal Builder UI to confirm end-to-end functionality.

---

## Verification

- Backtest result pages load for at least 2 known Signal Subscriptions.
- A new backtest run completes successfully and results persist to the database.
- No SQL connection errors in OTLP traces for 10 minutes post-restart.

---

## Rollback

If the restored database is invalid:

1. Stop the API App Service.
2. Revert Key Vault `SqlBacktestConnectionString` to the previous version.
3. Restart the API.
4. Re-run affected backtests from the Signal Builder as the recovery path if the original database is also corrupted.

---

## Post-incident

1. Write an `audit_events` record:
   - `event_type`: `bcp_restore_sql_backtest`
   - `restore_method`: `pitr` | `ltr_weekly` | `backtest_rerun`
   - `restored_to_timestamp`: (UTC, or `N/A` for re-run path)
   - `outcome`: `success` or `failure`
   - `operator`: admin identity
2. If this was a restore drill (`event_type: bcp_drill_sql_backtest`), link the drill record to the annual schedule.
3. Decommission the temporary restore database after validation.
4. If any drill step failed, open a Priority 1 follow-up.

---

**Last reviewed:** 2026-05-05 — drill-execution-001 (P8-T8); field-test required before Phase A user onboarding
