# Runbook 11 — PostgreSQL Restore (Market Data Database)

> **Note:** This runbook was originally written for Azure SQL Server and has been updated for the PostgreSQL migration. The codebase now uses PostgreSQL. Azure Database for PostgreSQL Flexible Server is the target platform.

**REQ coverage:** REQ-BCP-002, REQ-BCP-006  
**Last reviewed:** 2026-05-05 — drill-execution-001 (P8-T8); field-test required before Phase A user onboarding

---

## When to use

Open this runbook when:
- The `marketdata` PostgreSQL database is corrupted, missing, or has lost data that cannot be recovered via DataSync replay.
- An Azure Database for PostgreSQL failover has left the primary database in an inconsistent state.
- A restore drill exercise is being conducted per REQ-BCP-007.

**Note:** Per REQ-BCP-002, if the database is completely lost, DataSync and HistoricDataSeed can rebuild daily candles from the configured market data provider. Weekly and monthly candles are re-derivable from daily data. Evaluate whether a full restore is necessary before committing to the restore timeline.

---

## Preconditions

- You have contributor or owner access to the Azure Database for PostgreSQL Flexible Server resource group.
- The API and Worker services are shut down (see Step 1).
- You have identified the target point-in-time (UTC) to restore to, within the PITR retention window.
- DataSync has been confirmed as a rebuild path if the restore window is older than the retention period.

---

## Steps

### 1 — Shut down API and Worker

1. Stop the API App Service in the Azure portal.
2. Stop the Worker App Service.
3. Confirm no active DataSync or EODSR jobs are running (check `job_runs` MongoDB collection for `status: running` entries — update to `aborted` if found).
4. Record the shutdown time (UTC).

### 2 — Identify the restore target

**Option A — PITR restore (within retention window)**

1. Azure portal → Azure Database for PostgreSQL Flexible Server → `marketdata` database → Restore.
2. Choose "Point-in-time" restore.
3. Enter the target UTC timestamp (must be within the retention window, typically 7–35 days depending on configured backup retention).

**Option B — Long-term retention restore (weekly/monthly backup)**

1. Azure portal → Azure Database for PostgreSQL Flexible Server → `marketdata` server → Backups.
2. Select the appropriate weekly or monthly backup.
3. Restore from backup.

**Option C — Rebuild from DataSync and HistoricDataSeed (if backup is unavailable)**

1. Create a new empty `marketdata` database with the correct schema (run FluentMigrator migrations).
2. Run HistoricDataSeed (HDS) to repopulate OHLCV tables from the configured MDP.
3. Run DataSync to backfill the last 10 sessions.
4. Verify candle data completeness per Step 4.
5. No restore window required; this path is always available.

### 3 — Perform the restore

**PITR or LTR path:**

1. In the Azure portal, navigate to the PostgreSQL Flexible Server, click "Restore."
2. Enter server name for the restore target: `psql-marketdata-restore-<YYYYMMDD>`.
3. Confirm restore region is Central India (primary) per REQ-LEGAL-009.
4. Initiate restore and wait for completion (typically 15–45 minutes).

### 4 — Validate the restored database

1. **Row count check:** Query `SELECT COUNT(*) FROM d_reliance` (or a known high-volume symbol table) and compare against expected row count from the last known good backup.
2. **Date boundary check:** Confirm the latest candle date in each timeframe table (`d_`, `w_`, `m_`) matches the restore target date.
3. **Schema check:** Run `\dt` in psql or `SELECT table_name FROM information_schema.tables WHERE table_schema = 'public'` and confirm all expected symbol tables are present.
4. **Index integrity:** Run `SELECT schemaname, tablename, indexname, indexdef FROM pg_indexes WHERE tablename LIKE 'd_%' LIMIT 5;` to verify indexes exist.

### 5 — Update the connection string

1. In Key Vault, update the `SqlMarketDataConnectionString` secret to point to the restored server.
2. Confirm the new secret version is active.

### 6 — Restart API and Worker

1. Start the API App Service.
2. Confirm `/api/v1/healthz` returns 200.
3. Start the Worker App Service.
4. Confirm Worker heartbeat log appears.
5. Trigger a manual DataSync run via the admin portal to backfill any candles missing since the restore point.

---

## Verification

- Chart data loads for at least 5 symbols across all three timeframes (daily, weekly, monthly).
- DataSync completes a full run without error (check `job_runs` in MongoDB).
- No PostgreSQL connection errors in OTLP traces for 10 minutes post-restart.

---

## Rollback

If the restored database is invalid:

1. Stop API and Worker.
2. Revert Key Vault `SqlMarketDataConnectionString` to the previous version (original database).
3. Restart API and Worker.
4. If the original database is also unavailable, proceed with the DataSync rebuild path (Step 2, Option C).

---

## Post-incident

1. Write an `audit_events` record:
   - `event_type`: `bcp_restore_sql_marketdata`
   - `restore_method`: `pitr` | `ltr` | `datasync_rebuild`
   - `restored_to_timestamp`: (UTC, or rebuild start time)
   - `outcome`: `success` or `failure`
   - `operator`: admin identity
2. If this was a restore drill (`event_type: bcp_drill_sql_marketdata`), link the drill record to the annual schedule.
3. Decommission the temporary restore database after validation.
4. If any drill step failed, open a Priority 1 follow-up.

---

**Last reviewed:** 2026-05-05 — drill-execution-001 (P8-T8); field-test required before Phase A user onboarding
