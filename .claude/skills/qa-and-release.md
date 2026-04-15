# QA and Release Skill

Use this guide when setting up pipelines, writing test plans, or hardening release processes.
Requirements live in [docs/requirements-spec.md](../../docs/requirements-spec.md).

## Read These Sections First

- non-functional requirements
- market data and EOD sync
- portfolio analytics and accounting
- admin operations

## QA Focus

- test calculations and state transitions deeply
- prioritize auth, provider integration, reconciliation, and signal flows
- validate logging, trace correlation, and OTLP export behavior in critical environments
- make release risk visible before deployment
- keep docs aligned with shipped behavior

## Checklist

- critical user journeys have automated or explicit manual verification
- operational metrics and logs exist for new workflows
- migration and rollback considerations are reviewed for schema changes
