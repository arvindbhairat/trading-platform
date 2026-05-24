# Backend Foundation Skill

Use this guide when implementing backend APIs, persistence, domain logic, or worker-facing services.
Requirements live in [docs/requirements-spec.md](../../docs/requirements-spec.md).

## Read These Sections First

- canonical data ownership
- identity and access
- market data and EOD sync
- portfolio analytics and accounting
- admin operations
- configuration model

## Backend Focus

- start from domain model and API contract
- keep MongoDB and PostgreSQL ownership boundaries explicit
- keep ASP.NET Core and .NET worker implementation idiomatic to C#
- use `ILogger` in service code and keep Serilog/OpenTelemetry wiring in infrastructure
- implement service logic before transport glue
- make failure and recovery behavior traceable
- keep job contracts idempotent and observable

## Checklist

- model changes are explicit
- critical paths have error codes and logs
- persistence ownership is documented
- async flows include retry and success-marker strategy where needed
