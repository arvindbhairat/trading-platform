# Engineering Standards

## Purpose

This file defines reusable engineering conventions.
Product requirements belong in [requirements-spec.md](./requirements-spec.md), not here.

## General Standards

- prefer explicit, readable code over clever abstractions
- keep business rules in domain or service layers, not controllers or UI handlers
- isolate provider adapters from internal domain models
- keep MongoDB operational models separate from SQL Server historical models
- make important state transitions explicit and validated
- in .NET services, prefer `ILogger` in application code and keep Serilog behind the logging provider boundary

## API Standards

- version APIs from the start
- use typed request and response models at every boundary
- use consistent machine-readable error codes
- require idempotency or deduplication strategies for sensitive write flows
- document auth and permission requirements per endpoint

## Data Standards

- store timestamps in UTC
- use immutable audit and event records for critical actions
- use explicit enums or status models instead of free-form state strings
- avoid hard deletes for sensitive operational records when archival semantics are safer
- keep symbol-to-`sql_table_name` mapping deterministic and stable
- make trade-ledger ingestion append-only and idempotent where possible
- never silently rewrite historical trade-ledger records during reconciliation
- link manual adjustment entries to the affected holding or position context
- keep bootstrap configuration separate from mutable runtime configuration

## Background Job Standards

- keep jobs idempotent and retry-aware
- write explicit success and failure markers for important scheduled workflows
- require a verifiable EOD success marker before the EOD Signal Runner (EODSR) runs
- fail loudly when shared provider auth is unavailable for a required workflow
- keep retries, dead-letter handling, and observability intentional
- keep provider credentials out of job payloads unless absolutely necessary
- initialize logging before loading MongoDB-dependent runtime config

## Observability Standards

- use OpenTelemetry as the cross-system observability model
- export logs, traces, and metrics over OTLP
- prefer a stable collector or gateway endpoint between applications and vendors
- keep vendor routing out of application code where practical
- in .NET services, use Serilog for structured logging and enrich logs with service, environment, trace, job, and domain identifiers
- correlate logs with traces using standard OpenTelemetry trace context

## Configuration Standards

- keep startup-critical config in environment variables, Azure App Configuration, or equivalent bootstrap sources
- keep secrets in Azure Key Vault or equivalent secret stores, not in MongoDB `sys_config`
- use MongoDB `sys_config` only for mutable admin-managed runtime settings
- expose `sys_config` changes through audited admin screens instead of direct database edits

## Security Standards

- validate and authorize every sensitive server action
- never expose provider secrets to the client
- redact secrets and sensitive PII from logs
- encrypt FYERS credentials and restrict decryption paths tightly
- keep admin-scoped and user-scoped broker auth logically separated
- resolve the first admin from environment configuration, not open self-service elevation

## Frontend Standards

- treat the UI as a view over server truth
- show loading, stale, empty, and error states intentionally
- keep reconnect behavior explicit and user-friendly
- keep actions obvious, confirmable, and reversible where possible
- keep charting logic client-side when required by the charting library
- preserve important context around strategy, portfolio, and alert decisions

## Testing Standards

### Coverage targets

- unit test coverage minimum 70% for the .NET solution measured by line coverage; minimum 60% for the TypeScript portal codebase
- integration tests must cover every critical workflow end-to-end: OAuth signup, FYERS authentication, Signal Subscription creation and pause/resume, EOD Signal Runner full pass against a seeded universe, RME profile evaluation, order placement Phase 1 modal through to Phase 2 callback handling, account reconciliation, manual adjustment, and admin operations including kill-switch activation
- end-to-end tests must exercise at least the critical user journeys end-to-end through a real browser harness (Playwright or equivalent)
- regression tests are mandatory for any defect found in auth, sessions, FYERS integration, market data ingestion, RME, portfolio accounting, permissions, reconciliation, or notification delivery — the regression test must accompany the fix in the same pull request

### Test classes

- unit test calculations, rules, and state transitions
- integration test persistence-backed workflows across MongoDB and SQL Server
- end-to-end test the critical user journeys via a real browser harness
- contract test the Market Data Provider abstraction against each concrete provider implementation

### Performance and resilience

- a load test must be executed and recorded before the Phase B transition: simulate the Phase A approved-user ceiling at peak concurrent activity (dashboard load, chart navigation, intraday sync, LMDS active) and verify all SLO targets in the requirements spec hold; load-test results gate the Phase B transition per REQ-LEGAL-005 indirectly through REQ-NFR-014
- chaos and failure-injection exercises must be performed and recorded before the Phase C transition: simulate provider outages, MongoDB primary failover, Key Vault transient failures, and FYERS rate-limit responses; the platform must degrade gracefully in each scenario per REQ-NFR-003
- both load tests and chaos exercises must be recorded in `audit_events` with scope, outcome, and remediation tracking

## Market Data Provider (MDP) Standards

- implement market data access behind the Market Data Provider (MDP) common interface; no job, service, or domain layer may call a specific provider's API directly
- the provider interface must expose at minimum: fetch historical daily OHLCV candles for a symbol over a date range (batched), and fetch the latest quote for a symbol
- each concrete provider implementation (FYERS, TrueData, Global Data Feeds) must be independently testable against the same interface contract
- the active MDP implementation is resolved at startup from admin configuration; the rest of the system must not need to know which provider is active
- store provider credentials (API keys, secrets) in Key Vault or environment variables; never in `sys_config`, fixtures, or code
- batch HistoricDataSeed and DataSync calls to respect each provider's per-call data point limits and rate limits
- make the HistoricDataSeed job resumable: track the last successfully written date per symbol so the job can restart without re-fetching already-present data

## External API Integration Standards

- route all FYERS API calls through the centralised throttling layer; never call provider APIs directly from business or job code
- enforce per-second, per-minute, and per-day limits sourced from `sys_config` at the throttling layer boundary
- treat rate limit responses (HTTP 429 or provider-equivalent) as transient errors; apply exponential back-off with a configurable multiplier, initial delay, and maximum delay
- log the delay duration and reason whenever a request is held in the throttle queue beyond a configurable warning threshold
- fail a job loudly with a structured error if rate limiting prevents completion before the next scheduled run window; never silently skip work
- size batch operations (EOD sync, account sync, post-market scans) so total daily call consumption stays within the configured daily limit with headroom for user-initiated requests
- expose current-day API call consumption as an observable metric so operators can monitor headroom against the daily limit

## Operational Standards

- every major async workflow must be observable
- every external integration must have timeouts and retry rules
- every deployment must preserve migration safety
- every incident-prone area should have a runbook or troubleshooting note
- keep Azure DevOps pipelines reproducible and environment-specific
- keep Azure secrets and environment configuration out of repository code

## Documentation Standards

- update [requirements-spec.md](./requirements-spec.md) first when product behavior changes
- update architecture docs only for design changes
- update roadmap only for sequencing changes
- keep skill files short and derived from canonical docs
- record meaningful trade-off decisions in `docs/adr`
