# Observability

- **Vendor:** Honeycomb (migrated from New Relic May 2026)
- **Why:** New Relic free tier doesn't have MCP support; Honeycomb's MCP server allows the agent to pull logs, traces, and metrics directly
- **Pipeline:** .NET ILogger → Serilog → OpenTelemetry SDK → OTLP/HTTP → Honeycomb
- **Env vars:** `Telemetry__Otlp__Endpoint` (e.g. `https://api.honeycomb.io`) and `Telemetry__Otlp__ApiKey` (Honeycomb Ingest API key)
- **Auth header:** `x-honeycomb-team={ApiKey}` (set in `TelemetryBootstrapExtensions.cs`)
- **Signal paths:** `/v1/logs`, `/v1/traces`, `/v1/metrics` appended to base endpoint URL
- **Export protocol:** HTTP Protobuf (not gRPC)
- **Export timeout:** 5000ms (configurable via `ExportTimeoutMilliseconds`)
- **Agent MCP tool:** Honeycomb MCP server configured in Claude — use `list_spans`, `get_span_details`, `run_query`, `get_trace` for troubleshooting
- **Troubleshooting skill:** `.claude/skills/troubleshooting.md` references Honeycomb throughout

## ActivitySources (custom tracing)
- **API:** `SignalStack.Api` — traces auth.complete, auth.admin_recover, auth.accept_legal, execution.pre_flight, execution.order_context, execution.signed_payload, execution.intent_callback, execution.pending_confirmations
- **Worker:** `SignalStack.Worker` — traces worker.startup, worker.lease_refresh, datasync.poll_cycle, EODSR.poll_cycle, HDS.poll_cycle, LADS.poll_cycle, LMDS.poll_cycle, NDJ.poll_cycle, FyersTokenCheck.poll_cycle
- **Web (server):** Auto-instrumented via HttpInstrumentation; custom spans via `trace.getTracer("web-telemetry")` for telemetry.ingest

## Key Tags on Spans
- `enduser.id` — authenticated user ID (set by middleware in API)
- `enduser.role` — user role (set by middleware in API)
- `web.correlation_id` — browser correlation UUID
- `auth.provider` / `auth.outcome` / `auth.flow` — auth flow context
- `execution.*` — execution flow context (nonce, outcome, blocking_count)
- `datasync.*` / `eodsr.*` / `hds.*` / `lads.*` / `lmds.*` — per-job context
- `singleton.*` — worker lease context (acquired, retry_attempts, etc.)
- `session.validation_result` — session validation outcome (valid, missing_jti, revoked)
- `symbol` — trading symbol for relevant operations

## Custom Meters (business metrics)
### API
- `api.http.request.duration_ms`, `api.http.request.total` — request metrics by method/status
- `api.auth.login.success.total`, `api.auth.login.failed.total` — auth outcomes by provider
- `api.execution.pre_flight_check.total`, `api.execution.signed_payload.total`
- `api.portfolio.fetch.duration_ms`
- `api.session.validation.total`

### Worker
- `datasync.cycle.duration_ms`, `datasync.session.outcome.total`, `datasync.symbols_processed.total`
- `hds.seed.duration_ms`, `hds.symbols_seeded.total`
- `eodsr.run.duration_ms`, `eodsr.signals_generated.total`
- `lads.sync.duration_ms`
- `lmds.scan.duration_ms`, `lmds.positions_scanned.total`
- `ndj.delivery.duration_ms`, `ndj.delivered.total`
- `fyers_token_check.outcome.total`

## Npgsql Instrumentation
- Enabled via `AppContext.SetSwitch("Npgsql.EnableTelemetry", true)` in both API and Worker bootstrap
- Zero-code tracing — every NpgsqlCommand execution emits spans with `db.system`, `db.name`, `db.statement` attributes

## Web Browser → Backend Correlation
- `traceparent` header (W3C Trace Context) propagated on all apiFetch calls
- Auto-extracted by ASP.NET Core instrumentation for distributed trace joining
- Also sends `X-Correlation-Id` for legacy correlation via Honeycomb tags

## Error Span Events
Added `Activity.Current?.SetStatus(ActivityStatusCode.Error, ...)` and `Activity.Current?.AddException(ex)` in catch blocks across:
- API: SessionValidationMiddleware, auth endpoints, execution endpoints
- Worker: All 7 job services (DataSyncService, EodSignalRunnerService, HdsService, LadsService, LmdsService, WorkerHeartbeatService)
- Web: telemetry.ingest endpoint

## Web Error Boundaries
- `ErrorBoundary` (React class component with componentDidCatch) — wraps app content
- `error.tsx` — Next.js route segment error page
- `global-error.tsx` — Next.js root layout error page
- Browser-side console.warn/log/debug replaced with `telemetry.trackCustom()` or `serverLogger`

## Key Files
- `apps/api/Observability/ApiTelemetry.cs` — API ActivitySource + all meters
- `apps/api/Observability/TelemetryBootstrapExtensions.cs` — OTLP bootstrap
- `apps/worker/Observability/WorkerTelemetry.cs` — Worker ActivitySource + all meters
- `apps/worker/Observability/TelemetryBootstrapExtensions.cs` — OTLP bootstrap
- `apps/web/src/instrumentation/otel.ts` — Web OTel setup
- `apps/web/src/lib/telemetry.ts` — Browser telemetry client + traceparent
- `apps/web/src/lib/server-logger.ts` — Server-side OTel logging utility
- `apps/web/src/components/ErrorBoundary.tsx` — React render error catch
- `apps/web/src/app/error.tsx` — Route segment error page
- `apps/web/src/app/global-error.tsx` — Root layout error page
