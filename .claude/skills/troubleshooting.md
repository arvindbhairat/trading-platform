# Troubleshooting Agent — Debug, Diagnose & Fix Production Issues

## Overview

You are a troubleshooting agent for the **SignalStack** trading platform — 3 services deployed on Railway, observability exported via OTLP/HTTP to Honeycomb. You have access to a Honeycomb MCP tool to fetch logs, traces, and metrics from all 3 services.

This skill provides the systematic diagnostic workflow, common failure-mode catalog, and runbook cross-references needed to debug issues quickly and safely.

---

## 1. System Architecture (tl;dr)

| Service | Tech | Railway Service | Logs at |
|---|---|---|---|
| `apps/web` | Next.js 16, client-rendered SPA, no SSR/API routes | `web` | Browser console + Railway logs (no OTLP yet) |
| `apps/api` | ASP.NET Core 10 | `api` | Serilog → OTLP/HTTP → Honeycomb |
| `apps/worker` | .NET 10 Worker Service | `worker` | Serilog → OTLP/HTTP → Honeycomb |

**Deployment:** Railway auto-deploys from `main` via GitHub integration. All 3 services in the same Railway project.

**Observability pipeline:**
```
.NET ILogger → Serilog (structured) → OpenTelemetry SDK → OTLP/HTTP → Honeycomb
```
- OTLP endpoint: `Telemetry__Otlp__Endpoint` env var
- API key: `Telemetry__Otlp__ApiKey` env var
- Signal paths: `/v1/logs`, `/v1/traces`, `/v1/metrics` appended automatically
- All tokens/secrets are redacted from logs via `SensitiveDataRedactionLogProcessor`

---

## 2. Diagnostic Workflow

### Step 1 — Identify the affected surface

From the error report, determine which service(s) are involved:

| Symptom | Likely service(s) |
|---|---|
| HTTP error, wrong response, auth failure | `apps/api` |
| Background job not running, stale data, notification not sent | `apps/worker` |
| UI broken, chart not loading, blank page | `apps/web` (then trace API calls) |
| Everything down | Railway infrastructure |

### Step 2 — Fetch logs from Honeycomb

Use the Honeycomb MCP tool to fetch recent logs. Key query patterns:

```
# All errors across all services (last 15 min)
query: "level:Error"

# Errors for a specific service
query: "service:SignalStack.Api AND level:Error"

# Worker singleton violations
query: "singleton_violation OR PositionChannelRegistry"

# FYERS token issues
query: "fyers_token OR dirty_token OR token_refresh_failed"

# OTLP exporter failures
query: "Export failed OR OTLP exporter"

# Trace for a specific request (if you have a traceId)
query: "trace:abc123..."
```

### Step 3 — Trace the flow

For API issues:
1. Check the API endpoint handler in `apps/api/` matching the route (e.g., `/api/v1/portfolio/summary` → `apps/api/Portfolio/`)
2. Trace to the service layer in the relevant `packages/` project
3. Check data access (MongoDB repository in `packages/storage/` or PostgreSQL in `packages/historical/`)

For Worker issues:
1. Identify the job (DataSync, LADS, LMDS, EODSR, NotificationDelivery) in `apps/worker/Jobs/`
2. Check its scheduler/runner in `apps/worker/Runners/`
3. Check any shared services in `packages/`

For Web issues:
1. Check the Next.js page/component in `apps/web/src/app/` or `apps/web/src/features/`
2. Trace the API call via the client in `apps/web/src/lib/`
3. Check if the API backend returned an error (Honeycomb)

### Step 4 — Find affected code

Use the BUG-FIX-PROTOCOL (see `BUG-FIX-PROTOCOL.md` in root):
- If a REQ-ID is known: grep `execution_plan/task_logs/*.md` for that REQ-ID to find which files were changed and why
- If no REQ-ID: grep the source tree by error message text or component name

### Step 5 — Check configuration

- Env vars per service: see `.memory/railway_env_vars.md`
- Runtime settings: MongoDB `sys_config` collection (schema at `docs/system-config.md`)
- Bootstrap/LKG cache: `SignalStack:Bootstrap:LastKnownGood:CachePath`

### Step 6 — Check Railway status

- Railway dashboard: verify all 3 services are running and healthy
- Check `/api/v1/healthz` and `/api/v1/readyz` endpoints on the API
- Verify environment variables are set correctly per `.memory/railway_env_vars.md`

---

## 3. Common Failure Modes & Diagnostic Paths

### 3.1 Worker — Singleton Lease Violation

**Symptom:** Duplicate Worker instances detected, crash-loop, `singleton_violation` errors in logs.

**Diagnostic:**
- Check Honeycomb for `singleton_violation` or `singleton_lease_unavailable` log entries
- Verify Redis connection string (`ConnectionStrings__Redis`)
- Check `WorkerSingleton` config values in `appsettings.json`
- If Redis is unreachable, the Worker starts but logs a warning — check `position_concurrency_alert` in admin advisory

**Runbook:** `docs/operations/runbooks/rme-concurrency-freeze-resolution.md`

**Architecture:** ADR-0003, `apps/worker/Singleton/` subsystem.

### 3.2 Admin FYERS Token — Expired / Dirty

**Symptom:** DataSync, LMDS, EODSR not running. Admin dashboard banner. Telegram admin alert fired once.

**Diagnostic:**
- Honeycomb: search for `fyers_token`, `dirty_token`, `token_refresh_failed`
- Check `FYERS_ACCESS_TOKEN` and `FYERS_APP_ID` env vars in Railway
- The platform enters asymmetric degraded state: user-specific operations (LADS, chart quotes, exit advisories) continue, but shared-ingestion jobs (LMDS, DS, EODSR) suspend

**Runbook:** `docs/operations/runbooks/admin-fyers-token-reauth.md`

**Requirements:** REQ-MARKET-016, REQ-AUTH-003..010.

### 3.3 User FYERS Token — Dirty / Missing

**Symptom:** User sees "FYERS re-auth required" modal. Cannot view portfolio or use execution features.

**Diagnostic:**
- Check user's FYERS token state in MongoDB `fyers_tokens` collection
- The portal displays a full-screen modal that cannot be dismissed without completing re-auth
- Honeycomb: search for `fyers` AND `reauth` in API logs

**Architecture:** `docs/system-architecture.md` § Session and FYERS Lock Flow.

### 3.4 RME Concurrency Freeze (OCC Exhaustion)

**Symptom:** A position stops updating. Admin System Health panel shows frozen channel.

**Diagnostic:**
- Honeycomb: `OCC_exhausted` or `PositionChannelRegistry` or `rme_incidents`
- Check MongoDB `rme_incidents` collection
- Admin dashboard should show frozen position indicators

**Runbook:** `docs/operations/runbooks/rme-concurrency-freeze-resolution.md`

**Requirements:** REQ-RME-CONC-007 (`rme_incidents` collection), ADR-0003.

### 3.5 DataSync / EODSR Not Running

**Symptom:** No new OHLCV data. No entry signals generated.

**Diagnostic:**
- Check EOD pipeline sequence: DS must complete before EODSR starts
- Honeycomb: search for `DataSync` or `EodSignalRunner` — check for `Success` vs `Failed` markers
- Check that the admin FYERS token is valid (see 3.2)
- Verify `DataSync:PollIntervalSeconds` and `EodSignalRunner:PollIntervalSeconds` in config
- Check that the EOD `session_success_marker` exists in MongoDB

**Runbook:** `docs/operations/runbooks/datasync-failure-recovery.md`

**Architecture:** `docs/system-architecture.md` § EOD Sync and Scan Flow, Worker Service Schedule.

### 3.6 MongoDB Failover During LADS

**Symptom:** Three consecutive LADS aborts. Admin advisory raised.

**Diagnostic:**
- Honeycomb: search for `LADS` AND `abort` OR `retryWrites` OR `MongoConnectionException`
- Check MongoDB replica-set primary status
- Worker should retry with exponential backoff; after 3 consecutive aborts, it writes an admin advisory and pauses LADS

**Runbook:** `docs/operations/runbooks/mongodb-failover-lads-recovery.md`

**Requirements:** REQ-PORT-021a, REQ-BCP-001.

### 3.7 FYERS Rate Limiting

**Symptom:** 429 responses from FYERS. Jobs pause or skip work.

**Diagnostic:**
- Honeycomb: search for `rate_limit` OR `429` OR `throttle`
- Check `fyers-api-budget.md` in `docs/operations/`
- Verify rate-limit config in `sys_config`
- Batch sizes may be too large (`operations.universe.symbol_probe_batch_size`, etc.)

**Requirements:** REQ-RATE-011/012, `docs/operations/fyers-api-budget.md`.

### 3.8 OTLP Exporter Failure

**Symptom:** No logs in Honeycomb. Telemetry missing.

**Diagnostic:**
- Check `Telemetry__Otlp__Endpoint` and `Telemetry__Otlp__ApiKey` env vars
- Honeycomb endpoint format: `https://api.honeycomb.io` (appends `/v1/logs`, `/v1/traces`, `/v1/metrics`)
- Export timeout defaults to 5000ms (`ExportTimeoutMilliseconds`)
- The app logs a startup line: `"Telemetry config — Endpoint: {Endpoint}, ApiKey configured: {HasKey}"` — check Railway logs for this
- If OTLP endpoint is not set, OTel runs without the exporter (logs/metrics/traces are no-op)

### 3.9 CORS / Railway-Specific Issues

**Symptom:** Frontend can't reach API, preflight failures.

**Diagnostic:**
- Railway terminates TLS at edge, forwards HTTP to container
- Check `Cors:AllowedOrigins` env var — set to the Railway web URL
- The `X-Forwarded-Proto` header is read to restore `https` scheme
- Check `Auth__FrontendBaseUrl` is set to the Railway web URL
- Next.js `NEXT_PUBLIC_API_BASE_URL` resolves at build time — check it's correct

**Code:** `apps/api/Program.cs` lines 225-238 (CORS), 246-258 (X-Forwarded-Proto).

### 3.10 Session / Auth Issues

**Symptom:** Unexpected logouts, 401/403 errors.

**Diagnostic:**
- Honeycomb: search for `SessionValidationMiddleware` or `session_invalidated` or `jti`
- Data Protection keys persisted in Redis — check `ConnectionStrings__Redis` is set
- Session expiry: 24 hours. New login on second device invalidates the previous session server-side
- OAuth callback diagnostic logging is enabled — search for `OAuth callback` in API logs

**Requirements:** REQ-SESSION-001/002/002a, REQ-AUTH-001/002/011.

### 3.11 Intent-to-Order Reconciliation Gaps

**Symptom:** Orders submitted but not linked to platform intents. Unresolved/orphan intents.

**Diagnostic:**
- Honeycomb metrics: `order_reconciled_via_lads_only_total`, `order_unresolved_total`, `order_orphan_total`
- Check `intent_ledger` collection in MongoDB for `pending` records past `intent_timeout_minutes`
- High LADS-only reconciliation ratio suggests callback delivery regression from FYERS SDK
- Alerts at `sys_config.orders.callback_failure_alert_ratio` threshold

**Requirements:** REQ-ORDER-015d, REQ-PORT-031/031a.

### 3.12 Symbol Validity Probe Failures

**Symptom:** Admin work queue of rename/delisting candidates.

**Diagnostic:**
- Check `symbol_health` collection in MongoDB for high `consecutive_failure_count`
- Verify admin FYERS token is valid
- Check `symbol_probe_flag_threshold_days` and `symbol_probe_batch_size` in `sys_config`
- The probe runs once per trading day, triggered by first successful admin token generation

**Architecture:** `docs/system-architecture.md` § Symbol Validity Probe Flow.

---

## 4. Railway-Specific Diagnostic Commands

### Service URLs (from Railway env):

| Service | URL |
|---|---|
| API | `https://api-production-b8b8f.up.railway.app` |
| Web | `https://web-production-0c8143.up.railway.app` |

### Health endpoints:

```bash
# API health check
curl https://api-production-b8b8f.up.railway.app/api/v1/healthz

# API readiness check (includes seed_version_sentinel)
curl https://api-production-b8b8f.up.railway.app/api/v1/readyz

# API root info
curl https://api-production-b8b8f.up.railway.app/api/v1
```

### Railway GitHub Integration:
- Railway watches `main` branch and auto-deploys on push
- No GitHub Actions deployment workflow — Railway handles it natively
- Pre-deploy: `.github/workflows/deploy-railway.yml` runs `sys_config` seeder manually via `workflow_dispatch`

---

## 5. Key Env Vars to Verify

All configured values tracked in `.memory/railway_env_vars.md`.

| Variable | Services | Purpose |
|---|---|---|
| `Telemetry__Otlp__Endpoint` | API, Worker | Honeycomb OTLP ingest URL |
| `Telemetry__Otlp__ApiKey` | API, Worker | Honeycomb Ingest API key |
| `ConnectionStrings__MongoDb` | API, Worker | MongoDB connection string |
| `ConnectionStrings__Redis` | API, Worker | Redis connection string |
| `ConnectionStrings__SqlServer` | API, Worker | PostgreSQL connection string (historical data) |
| `FYERS_APP_ID` | API, Worker | FYERS app identifier |
| `FYERS_APP_SECRET` | API | FYERS app secret |
| `Auth__Jwt__Secret` | API | JWT signing key |
| `Auth__SeedAdminEmail` | API | First admin email for bootstrapping |
| `TELEGRAM_BOT_TOKEN` | Worker | Telegram notification bot token |
| `API_BACKEND_URL` | Web | API backend URL (read at container start) |
| `Cors__AllowedOrigins__0` | API | Web frontend URL for CORS |

---

## 6. Fix Workflow

1. **Read BUG-FIX-PROTOCOL.md** (root) for the 7-step code-discovery procedure
2. **Load domain context** from the matching skill file in `.claude/skills/`:
   - API/domain → `backend-foundation.md`
   - Portal/UI → `frontend-trading-ui.md`
   - Market data/FYERS → `market-data-and-orders.md`
   - Strategy/backtest → `strategy-and-backtesting.md`
   - Auth/integrations → `identity-and-integrations.md`
   - Admin/ops → `admin-and-operations.md`
   - RME/risk → `risk-and-compliance.md`
3. **Make minimal changes** — one atomic fix per issue
4. **Add or update tests** in the matching `tests/` project
5. **Log the fix** — write a task log to `execution_plan/task_logs/` following the BUG-FIX-PROTOCOL format
6. **Verify** in Railway staging after deploy

---

## 7. References

| Resource | Location |
|---|---|
| Bug-fix protocol | `BUG-FIX-PROTOCOL.md` (root) |
| Operational runbooks | `docs/operations/runbooks/README.md` |
| Architecture & flows | `docs/system-architecture.md` |
| Requirements (REQ-IDs) | `docs/requirements-spec.md` |
| Runtime config schema | `docs/system-config.md` |
| Data collection catalogue | `docs/data-management.md` |
| Repo navigation map | `docs/project-structure.md` |
| Railway env vars | `.memory/railway_env_vars.md` |
| Architecture decisions | `docs/adr/` |
| FYERS API reference | `docs/fyers_api_integration_guide.md` |
| Engineering standards | `docs/engineering-standards.md` |
| REQ-ID → file mapping | Grep `execution_plan/task_logs/*.md` for the REQ-ID |
