# ADR 0002: OpenTelemetry Platform Standard with Serilog in .NET Services

## Status

Accepted (revised 2026-05-24 — removed OTel collector sidecar; apps export OTLP/HTTP directly)

## Context

The platform uses:

- Next.js for the unified web frontend
- ASP.NET Core and .NET worker services for backend APIs, jobs, and backtesting

The system needs:

- consistent observability across all applications
- a simple setup that works with the developer's chosen observability vendor (initially New Relic, migrated to Honeycomb May 2026)
- centrally managed configuration
- admin-managed runtime settings without direct database edits

## Decision

Use the following model:

- OpenTelemetry is the cross-system observability standard
- OTLP is the export protocol for logs, traces, and metrics
- .NET services use `ILogger` with Serilog as the logging provider
- applications export OTLP/HTTP directly to the configured vendor endpoint, passing the API key as a header — no intermediate collector or gateway is deployed
- the OTLP endpoint and API key are configuration values; changing vendors requires a config update and is not a zero-touch operation (accepted trade-off for solo-POC scope)
- startup-critical configuration comes from environment variables, Azure App Configuration, and secret stores such as Azure Key Vault
- mutable admin-managed runtime settings are stored in MongoDB `sys_config`
- seeded admin email and connection strings remain outside MongoDB

## App Configuration startup-failure policy

Azure App Configuration is a startup dependency for both the API service and the Worker Service. The platform must handle App Config unreachability at startup and mid-process without silent degradation or undefined behaviour. The following policy is binding:

**At startup — App Config unreachable:**
1. The service attempts to connect to Azure App Configuration at the configured endpoint.
2. On connection failure, the service checks for a local last-known-good (LKG) cache file at a process-local directory path determined by the `APPCONFIG_LKG_CACHE_PATH` environment variable (required at deployment; must be a writable local path, not a MongoDB or network path — the LKG cache must be reachable without any network I/O to preserve the guardrail that startup-critical config must not depend solely on a remote service).
3. **If an LKG cache exists and its age is within the threshold configured in `sys_config` under `config.last_known_good.max_age_seconds` (default 86400 — 24 hours):** the service warm-starts from the cache, logs a structured Warning (`config.source: lkg_cache; config.source.app_config_unreachable: true`), and emits an OpenTelemetry gauge metric `config.source.last_reached_seconds` with the elapsed seconds since the timestamp recorded in the cache. The admin System Health widget (REQ-ADMIN-014) must surface any service running from an LKG cache with a visible warning.
4. **If no LKG cache exists, or the cache is older than the configured max age:** the service must refuse to start, log a structured Error naming the missing or stale cache, and exit with a non-zero status code so the process supervisor produces a visible crash-loop signal. This is a hard-fail — starting from unbounded-stale or absent configuration is more dangerous than failing closed.

**During normal operation — LKG cache maintenance:**
- On every successful read from Azure App Configuration (at startup or during a periodic refresh), the service writes the full resolved configuration snapshot to the LKG cache file, overwriting the previous version. The file must be written atomically (write to a temp file, then rename) to prevent a partial write from corrupting the cache. The file must include a timestamp field recording the time of the successful read.

**Worker Service — interaction with Redis singleton lease:**
- If the Worker Service warm-starts from the LKG cache, it must still attempt to acquire the Redis singleton lease (REQ-RME-CONC-006) before proceeding. A warm-start from LKG cache does not bypass the singleton invariant.
- If both App Config and Redis are unreachable at Worker startup with a valid LKG cache present, the Worker must warm-start with the LKG config and log `singleton_lease_unavailable` at Warning level per REQ-RME-CONC-006, consistent with the existing Redis-unreachable-at-startup behaviour.

**OTEL metric name**: `config.source.last_reached_seconds` — a gauge emitted by each service instance at startup and refreshed on each config read cycle. Zero means App Configuration was reached on the last attempt. Positive values indicate seconds since the last successful reach. A value above the LKG max-age threshold is an operational alert signal.

**LKG staleness alert threshold:** The `config.source.last_reached_seconds` gauge alone is insufficient for proactive operations visibility — the LKG cache may keep services running for up to 24 hours with no alert if App Config is unreachable. The following alerting layer is therefore required:

- A `config.last_known_good.staleness_alert_threshold_seconds` key must be seeded in `sys_config` (default 21600 — 6 hours). When any service instance's `config.source.last_reached_seconds` gauge exceeds this threshold on a refresh cycle, the platform must:
  1. Display a persistent non-dismissible banner in the admin System Health widget stating that Azure App Configuration has not been reached for more than the threshold duration, naming the service instance and the elapsed time.
  2. Emit a one-time `config.app_config_unreachable_threshold_exceeded` admin notification delivered via both Telegram and email. A repeat notification must not be emitted until the service successfully reaches App Config again and then loses contact a second time (edge-triggered, not level-triggered).
- The threshold must be strictly less than `config.last_known_good.max_age_seconds` (default 86400). A deployment that seeds a threshold ≥ the max-age is misconfigured; the Worker and API services must log a Warning on startup if this condition is detected.
- This threshold is a `sys_config` Tier 3 key (admin-managed runtime setting) and does not need to be bootstrap-available. Its absence or a MongoDB read failure must be treated as equivalent to the default (21600 s); the alert machinery must degrade safely rather than failing the service.

**Required sys_config seed key**: `config.last_known_good.max_age_seconds` (default 86400, type integer). This key is bootstrap-tier only — it must be seeded from the LKG cache or environment variables, not read from MongoDB, since MongoDB may also be unavailable at startup.

## Consequences

Positive:

- no collector infrastructure to deploy, configure, or monitor
- single configuration step: set endpoint URL and API key
- .NET services stay idiomatic for C# developers
- admin-editable runtime settings can be managed through the portal
- services can survive transient Azure App Configuration outages without restarting, using a bounded-age LKG cache

Trade-offs:

- changing observability vendors requires a code/config change rather than just a collector config change (acceptable for solo-POC scope)
- no local buffer for telemetry under saturation — if the vendor endpoint is slow or unreachable, telemetry is dropped at the application tier (acceptable for solo-POC scope; a collector sidecar can be reintroduced later if needed)
- configuration is split across bootstrap config and runtime config by design
- developers must keep startup config and mutable runtime config clearly separated
- a new `APPCONFIG_LKG_CACHE_PATH` environment variable is required at every deployment target; failing to set it means no LKG cache is written and a first-ever App Config outage becomes a hard startup failure
