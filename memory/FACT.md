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
