# DataSync Failure Recovery

**Phase required:** A
**Last reviewed:** *(not yet reviewed — stub)*
**Owner:** Platform admin

## When to use

Open this runbook when any of these occur:

- Admin portal System Health widget shows DataSync (DS) in a warning or critical state.
- The DataSync admin-only operational notification (per REQ-NOTIFY-006a) is received via Telegram or email.
- The morning after a trading session, dashboard data freshness timestamps show stale prices across multiple symbols.
- The EOD Signal Runner (EODSR) reports "dependency not ready" because the DataSync success marker for the previous session is missing.

## Preconditions

Before starting, verify:

1. Admin is signed into the portal with step-up re-authentication still valid (REQ-SEC-011).
2. The current active market data provider is known (check `sys_config` key `market_data.provider.active`).
3. If the active provider is FYERS, the admin's FYERS token is valid and non-dirty. If it's dirty, run the `admin-fyers-token-reauth.md` runbook first before continuing here.
4. The internal trading calendar has a session record for the affected date (REQ-CALENDAR-005). If the affected date is a calendar holiday, DataSync correctly skipped it; no recovery is needed.

## Steps

### Step 1 — Identify the failure scope

Navigate to **Admin → Jobs → DataSync** and review the most recent `job_runs` record. Note:

- The failure mode: authentication error, provider unavailable, rate limit exhausted, data quality threshold breached (REQ-MARKET-015), or symbol-level failures only.
- The affected session date.
- Whether any candles were written at all, or zero.
- Error details from the observability pipeline (logs + traces).

### Step 2 — Classify and branch

**If authentication error:** provider token is dirty. Run `admin-fyers-token-reauth.md` (for FYERS) or check `sys_config` provider credentials in Key Vault. Return here once the provider is re-authenticated.

**If provider unavailable (HTTP 5xx, timeouts):** check the provider's status page. Wait, then retry. If the outage is prolonged (over 2 hours into the post-market window) and the active provider is switchable, consider temporarily flipping `market_data.provider.active` to the backup provider via admin sys_config edit (requires step-up). Record the rationale in `audit_events`.

**If rate limit exhausted:** verify the current-day consumption against the budget model in `docs/operations/fyers-api-budget.md`. If consumption was driven by LMDS or LADS, no DataSync recovery is possible today — it will complete tomorrow when the daily counter resets. Notify affected users via the standard platform notification if the stale data materially impacts them.

**If data quality threshold breached (REQ-MARKET-015):** the job aborted deliberately to prevent bad data. Do not retry blindly. Inspect the invalid candles in the job log, correlate against the provider's public data or another source, and only after identifying the root cause retry the job. If the provider is faulty, consider switching providers before retry.

**If symbol-level failures only:** DataSync by default skips problem symbols and continues (REQ-MARKET-006). Verify the EODSR proceeded for all other symbols. For the failed symbols, decide whether a targeted reseed is needed via the HistoricDataSeed job (REQ-UNIV-015b).

### Step 3 — Retry DataSync

From **Admin → Jobs → DataSync → Trigger manually**, launch a retry. The portal blocks duplicate triggers per REQ-MARKET-008 so this cannot create concurrent runs. The retry uses the same 10-session recovery window (REQ-MARKET-009) and will gap-fill any missed sessions automatically.

### Step 4 — Verify EODSR can proceed

Once DataSync completes successfully and writes its success marker, the EODSR becomes eligible to run. From **Admin → Jobs → EOD Signal Runner**, trigger it manually if it did not auto-trigger on the DataSync success event. Confirm EODSR completes without the "dependency not ready" error.

### Step 5 — Validate

From the admin portal, confirm:

- DataSync `job_runs` shows outcome `success` for the affected session.
- EODSR `job_runs` for the same session shows outcome `success`.
- Dashboard data freshness timestamps refresh across affected users.
- No lingering admin-only operational notifications remain unresolved for DataSync.

## Rollback

DataSync writes are candle-level upserts with idempotent behaviour (REQ-MARKET-010). A partial run does not corrupt existing data; re-running completes gap fills. If bad data was written and identified during Step 2's data quality investigation, use the HistoricDataSeed job with an explicit from-date parameter (REQ-MARKET-010) to overwrite rows for the specific affected symbols.

Do not delete rows directly from SQL Server. Always use HistoricDataSeed for corrections.

## Post-incident

- Record the outcome and any sys_config changes you made in `audit_events`. This happens automatically for admin portal actions, but if you made any manual interventions outside the portal, note them separately.
- If the failure exposed a gap in monitoring or alerting, open an issue and update this runbook.
- If the failure was caused by provider behaviour not previously seen, note the specific provider response pattern in the MDP adapter's test fixtures so future regressions are caught earlier.
- Update the **Last reviewed** date at the top of this runbook.

## Notes

- The 10-session recovery window (REQ-MARKET-009) means a single missed day is usually self-healing on the next successful run. This runbook is for failures that persist beyond that automatic recovery.
- If DataSync is failing repeatedly during Phase A and the root cause is a FYERS daily-token problem, consider promoting the personal-FYERS-app replacement to an earlier milestone — it's a known Phase A limitation (REQ-LEGAL-002) and pulling it forward is always safe.
