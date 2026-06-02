# Telemetry

- **Vendor:** Honeycomb (migrated from New Relic May 2026)
- **Why:** New Relic free tier doesn't have MCP support; Honeycomb's MCP server allows the agent to pull logs, traces, and metrics directly
- **Pipeline:** .NET ILogger → Serilog → OpenTelemetry SDK → OTLP/HTTP → Honeycomb
- **Env vars:** `Telemetry__Otlp__Endpoint` (e.g. `https://api.honeycomb.io`) and `Telemetry__Otlp__ApiKey` (Honeycomb Ingest API key)
- **Auth header:** `x-honeycomb-team={ApiKey}` (set in `TelemetryBootstrapExtensions.cs`)
- **Signal paths:** `/v1/logs`, `/v1/traces`, `/v1/metrics` appended to base endpoint URL
- **Export protocol:** HTTP Protobuf (not gRPC)
- **Export timeout:** 5000ms (configurable via `ExportTimeoutMilliseconds`)
- **Build tracking:** Every service reads `RAILWAY_GIT_COMMIT_SHA` env var (set automatically by Railway) and adds it as `build_id` resource attribute on all spans, logs, and metrics. This allows Honeycomb queries to filter by deployment commit — the attribute name was changed from `git.commit.sha` to `build_id` on 2026-06-03.
- **Agent MCP tool:** Honeycomb MCP server configured in Claude — use `list_spans`, `get_span_details`, `run_query`, `get_trace` for troubleshooting
- **Troubleshooting skill:** `.claude/skills/troubleshooting.md` references Honeycomb throughout
- **Web cache busting:** `next.config.ts` uses `generateBuildId` returning `RAILWAY_GIT_COMMIT_SHA`, so every deploy produces unique `_next/static/<buildId>/` paths and never serves stale JS/CSS.

## Resource Attributes per service
- **API** (`TelemetryBootstrapExtensions.cs`): `service.name=SignalStack.Api`, `service.version=v0.2`, `deployment.environment`, `build_id`
- **Worker** (`TelemetryBootstrapExtensions.cs`): `service.name=SignalStack.Worker`, `service.version=v0.2`, `deployment.environment`, `build_id`
- **Web** (`otel.ts`): `service.name=SignalStack.Web`, `deployment.environment`, `build_id`