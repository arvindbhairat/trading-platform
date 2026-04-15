# ADR 0002: OpenTelemetry Platform Standard with Serilog in .NET Services

## Status

Accepted

## Context

The platform uses:

- Next.js for the unified web frontend
- ASP.NET Core and .NET worker services for backend APIs, jobs, and backtesting

The system needs:

- consistent observability across all applications
- vendor portability for Better Stack, Axiom, Grafana Cloud, or similar backends
- centrally managed configuration
- admin-managed runtime settings without direct database edits

## Decision

Use the following model:

- OpenTelemetry is the cross-system observability standard
- OTLP is the export protocol for logs, traces, and metrics
- .NET services use `ILogger` with Serilog as the logging provider
- applications send telemetry to a stable OTLP collector or gateway endpoint
- vendor routing is handled by collector or gateway configuration rather than application code
- startup-critical configuration comes from environment variables, Azure App Configuration, and secret stores such as Azure Key Vault
- mutable admin-managed runtime settings are stored in MongoDB `sys_config`
- seeded admin email and connection strings remain outside MongoDB

## Consequences

Positive:

- vendor switching becomes a configuration change rather than a code rewrite
- .NET services stay idiomatic for C# developers
- frontend and backend share one observability model
- admin-editable runtime settings can be managed through the portal

Trade-offs:

- configuration is split across bootstrap config and runtime config by design
- OTLP collector or gateway infrastructure becomes an important dependency
- developers must keep startup config and mutable runtime config clearly separated
