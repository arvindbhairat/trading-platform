# Chaos Engineering Exercise Plan

**REQ coverage:** REQ-NFR-014 (chaos / failure-injection exercises — Phase C gate)  
**Runbook #:** 20  
**Status:** Authored in P8-T12 — first execution recorded 2026-05-05  
**Last reviewed:** 2026-05-05 (initial)

---

## Purpose

Chaos and failure-injection exercises validate that the platform degrades gracefully
when external dependencies fail. Exercises defined here are mandatory before the
Phase C transition per REQ-NFR-014 and `docs/engineering-standards.md` § Performance
and resilience.

Each exercise defines a failure scenario, the expected graceful-degradation behaviour,
and pass/fail criteria. Exercises may be run as walkthroughs (verify runbook
correctness without touching real infrastructure), simulations (inject faults in a
staging environment), or live drills (execute against production-readiness environment
with real failure modes).

---

## Exercise catalogue

| # | Scenario | Graceful-degradation expectation | Type |
|---:|---|---|:---:|
| 1 | Market data provider (MDP) outage — LMDS / DS / EODSR lose access to FYERS REST API | LMDS pauses with `mdp_unavailable` status; DS marks current run `partial_completion_provider_unavailable`; EODSR refuses to start. User-token paths (live chart WebSocket, portal quotes) continue unaffected. Admin dashboard shows `market_data_provider.status = unavailable`. One-time Telegram alert fires. | Walkthrough → Live |
| 2 | MongoDB primary failover during market hours | API and Worker experience retryable write failures for ≤ 15 s during replica-set election. RetryWrites in flight succeed after election. Sustained failure (> 3 consecutive LADS aborts) triggers LADS graduated suspension + admin advisory per runbook #5. No position data loss. Stale reads from secondaries (if any) do not affect risk decisions. | Walkthrough → Live |
| 3 | Key Vault transient failure / throttling (HTTP 429 / 5xx) | At startup, LKG cache serves last-known config values for up to `config.last_known_good.max_age_seconds`. Staleness alert fires at `config.last_known_good.staleness_alert_threshold_seconds`. Running services continue with in-memory config. No crash-loop on transient failure. | Walkthrough |
| 4 | FYERS rate-limit 429 response during LMDS bulk-quote poll | Throttle layer respects `Retry-After` header. LMDS skips the affected poll cycle. `fyers_rate_limit_hit` metric increments. If consecutive rate-limit responses exceed threshold, LMDS logs `rate_limit_sustained` and emits admin advisory (no hard suspension — rate-limit budgets recover). | Walkthrough |
| 5 | RPO/RTO recovery exercise — MongoDB during market hours | Simulate MongoDB data loss during market hours. Validate RPO ≤ 1 min and RTO ≤ 15 min using the MongoDB PITR restore runbook (#10) with a timed recovery. Record the recovery time and data-loss window. | Walkthrough → Live (Phase B) |

---

## Exercise 1 — MDP outage (FYERS REST unavailable)

### Preconditions

- Staging or production-like environment with LMDS, DS, and EODSR configured
- Admin dashboard accessible
- Admin FYERS token is valid (or the outage is pre-arranged with the provider)

### Steps

1. Simulate FYERS REST API outage: block outbound traffic to `api.fyers.in` at the
   network layer (Azure NSG rule or local `hosts` override in the Worker container).
2. Observe LMDS behaviour:
   - LMDS should log `mdp_unavailable` within one poll cycle
   - In-cluster LMDS should pause quote acquisition (not crash-loop)
   - LMDS should remain in `paused` state, not `failed`
3. Observe DS behaviour:
   - DS should log `partial_completion_provider_unavailable`
   - DS should not mark the run `failed` — it should be `partial` with the reason
4. Observe EODSR behaviour:
   - EODSR should refuse to start if the MDP is unavailable at its scheduled time
5. Remove the network block and confirm:
   - LMDS resumes quoting within one poll cycle
   - DS may be manually re-triggered for the affected session
   - EODSR is unblocked for the next scheduled run

### Pass/fail

| Criterion | Expected | Result |
|---|---|---|
| LMDS remains running (paused, not crashed) | True | |
| DS run outcome is `partial` with provider_reason | True | |
| EODSR refuses to start | True | |
| User WebSocket quotes continue | True | |
| Admin dashboard MDP status = unavailable | True | |
| Admin Telegram alert fires | True | |

---

## Exercise 2 — MongoDB primary failover

### Preconditions

- MongoDB replica set with at least 3 members (M10+)
- API and Worker running with `retryWrites=true`
- Active LMDS polling and LADS sync running
- At least one open position in the portfolio

### Steps

1. Force a step-down of the MongoDB primary:
   ```
   db.adminCommand({ replSetStepDown: 30, secondaryCatchUpPeriodSecs: 15 })
   ```
2. Monitor during the election window (≤ 15 s):
   - API may return 503 for write-heavy endpoints; reads continue from secondaries
   - Worker channel consumers may see `MongoWriteException` — retry logic applies
   - LADS: if the failover window exceeds `jobs.account_sync.abort_threshold_consecutive`,
     LADS enters graduated suspension
3. After election completes (new primary elected):
   - Verify `retryWrites` reconnects successfully
   - Verify no position data was corrupted or lost
   - Verify LADS resumes normal polling after the abort condition clears
4. If LADS was suspended, follow runbook #5 (MongoDB failover / LADS recovery).

### Pass/fail

| Criterion | Expected | Result |
|---|---|---|
| Election completes within 15 s | True | |
| No position document corruption | True | |
| LADS resumes within 1 recovery cycle | True | |
| API health endpoint returns 200 after election | True | |
| Audit event for `mongodb_failover_detected` recorded | True | |

---

## Exercise 3 — Key Vault transient failure

### Preconditions

- API and Worker configured with Azure App Configuration + Key Vault reference
- LKG cache configured with `config.last_known_good.max_age_seconds` (e.g. 300)
- Staleness alert threshold set (e.g. 600)

### Steps

1. Revoke the managed identity's Key Vault access temporarily (remove the
   `Key Vault Secrets User` role assignment).
2. Observe:
   - API and Worker continue running with LKG-cached values
   - `KeyVaultAccessFailed` OTLP metric fires
   - After `staleness_alert_threshold_seconds`, a staleness alert fires
3. Restore the role assignment:
   - API/Worker fetch current values from Key Vault without restart
   - LKG cache is refreshed
   - Staleness alert clears

### Pass/fail

| Criterion | Expected | Result |
|---|---|---|
| No crash-loop during Key Vault outage | True | |
| LKG cache serves config within max_age | True | |
| Staleness alert fires after threshold | True | |
| Automatic recovery after access restored | True | |

---

## Exercise 4 — FYERS rate-limit 429

### Preconditions

- Worker running with LMDS active
- FYERS throttle configured with rate-limit budgets per `fyers-api-budget.md`

### Steps

1. Inject a 429 response on the FYERS bulk-quotes endpoint. In a test environment,
   this can be done via a proxy that intercepts the FYERS API call and returns 429
   with a `Retry-After` header (e.g. 5 seconds).
2. Observe:
   - Throttle layer logs `rate_limit_exceeded` with the `Retry-After` value
   - LMDS skips this poll cycle and logs `lmds_poll_skipped_rate_limit`
   - No crash or exception escalation
   - On the next poll cycle, LMDS resumes normal operation
3. If sustained rate-limiting is desired, repeat the 429 response for 3 consecutive
   cycles. The fourth should increment the `rate_limit_sustained` metric and trigger
   an admin advisory.

### Pass/fail

| Criterion | Expected | Result |
|---|---|---|
| No exception escalation beyond throttle layer | True | |
| LMDS skips one poll cycle gracefully | True | |
| LMDS resumes on next cycle | True | |
| Sustained rate-limit triggers admin advisory | True | |

---

## Exercise 5 — MongoDB RPO/RTO recovery drill (market hours)

### Preconditions

- See runbook #10 (MongoDB PITR restore)
- This exercise is conducted during the annual restore drill or as a standalone
  timed exercise before Phase C entry

### Steps

Follow runbook #10 (mongodb-pitr-restore.md) with timed measurement:

1. Record start time
2. Identify the restore target timestamp (≤ 1 min RPO)
3. Restore to new cluster (RTO ≤ 15 min target)
4. Validate restored dataset
5. Update connection string and restart services
6. Verify health and reconciliation

### Pass/fail

| Criterion | Expected | Result |
|---|---|---|
| RPO ≤ 1 min achieved | True | |
| RTO ≤ 15 min achieved | True | |

---

## Exercise execution record format

Each exercise execution must be recorded in `audit_events` (via the admin portal
chaos exercise endpoint or directly) with:

- `action_type`: `chaos_exercise_executed`
- `actor_id`: admin user who conducted the exercise
- `details.exercise_number`: the exercise number from the catalogue (1–5)
- `details.exercise_name`: short name from the catalogue
- `details.outcome`: `pass` / `fail` / `partial` / `walkthrough`
- `details.execution_type`: `walkthrough` / `simulation` / `live`
- `details.findings`: free-text notes about what was observed
- `details.remediation`: any follow-up actions required

Additionally, the overall gate-satisfaction record may be set via the admin portal's
Phase C gate controls once all five exercises have been executed with pass outcomes.

---

## Annual review

This runbook must be reviewed annually and after any infrastructure change that
affects the platform's dependency footprint (provider swap, database migration,
Key Vault architecture change). Each review must be recorded in `audit_events`
with `action_type: runbook_reviewed` and the runbook identifier.
