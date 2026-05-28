# Engineering Standards

## Purpose

This file defines reusable engineering conventions.
Product requirements belong in [requirements-spec.md](./requirements-spec.md), not here.

## General Standards

- prefer explicit, readable code over clever abstractions
- keep business rules in domain or service layers, not controllers or UI handlers
- isolate provider adapters from internal domain models
- keep MongoDB operational models separate from PostgreSQL historical models
- make important state transitions explicit and validated
- in .NET services, prefer `ILogger` in application code and keep Serilog behind the logging provider boundary

## API Standards

- version APIs from the start
- use typed request and response models at every boundary
- use consistent machine-readable error codes
- require idempotency or deduplication strategies for sensitive write flows
- document auth and permission requirements per endpoint

## API Contract Standards

### JSON naming convention

- All HTTP JSON property names use **snake_case** (e.g., `signal_type_id`, `rme_configuration`)
- Enforced globally via `JsonNamingPolicy.SnakeCaseLower` in `ConfigureHttpJsonOptions`
- `PropertyNameCaseInsensitive = true` is also set for defense-in-depth (accepts camelCase or mixed-case requests)
- This applies to all ASP.NET Core endpoint parameter binding (Path A) automatically
- Endpoints using `ReadFromJsonAsync<T>()` (Path B) are unaffected and should be migrated to endpoint parameter binding where possible

### Source of truth

- C# typed records are the authoritative definition of the API contract
- OpenAPI spec is generated from the API code at runtime via `/openapi/v1.json`
- Frontend TypeScript types should be derived from the OpenAPI spec (via `openapi-typescript`), not hand-written

### Contract enforcement

- `*.contract.json` files serve as a human-readable subset of the API contract
- `contract-diff` validates contract files against the live OpenAPI spec
- CI enforces that committed generated types match the OpenAPI spec

### Scope

| Area | In scope? |
|------|-----------|
| ASP.NET Core API HTTP contract (request + response) | ✅ In scope |
| Worker app (`apps/worker/`) serialization | ❌ Separate process, own config |
| FYERS API calls via `HttpClient` | ❌ Separate `JsonSerializerOptions` |
| MongoDB BSON serialization | ❌ Different serializer |
| Redis push event serialization | ✅ Already uses `SnakeCaseLower` explicitly |
| OAuth middleware | ❌ Middleware internals |

## Data Standards

- store timestamps in UTC
- use IST (Asia/Kolkata, UTC+05:30) as the canonical trading time zone for all scheduling, display, time-stop evaluation, and NSE trading-calendar logic; all components that consume time (EODSR, LMDS, LADS, the Timeframe/Calendar Layer, and the RME) must agree on this zone; stored timestamps are always UTC — conversion to IST happens at the presentation or scheduling layer only
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
- export logs, traces, and metrics over OTLP/HTTP
- export OTLP/HTTP directly to the configured vendor endpoint, passing the API key in the `api-key` header
- no intermediate collector or gateway is deployed (accepted trade-off for solo-POC scope; a collector sidecar can be reintroduced later if needed)
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
- **CSRF model.** Portal-to-API authentication uses Bearer JWT tokens carried in the HTTP `Authorization` header; cross-site requests cannot set the `Authorization` header, making this pattern inherently CSRF-safe. OAuth session state is maintained in HttpOnly, SameSite=Strict cookies that are never used as the sole credential for state-changing API endpoints — the Bearer token is always required and must be validated server-side before any mutation is applied. CSRF tokens (REQ-SEC-001) remain as a mandatory defence-in-depth layer on top of this model. Any endpoint that deviates from Bearer-in-header authentication (for example, a webhook receiver or a callback from FYERS) must explicitly document its authentication model and its CSRF mitigation in the endpoint's code comment and in the relevant requirement.
- **Token and secret redaction in structured logs.** Every .NET service must configure a Serilog destructuring policy that redacts the *value* of any log property whose name — case-insensitively — matches any of the following patterns: `*token*`, `*access_token*`, `*authorization*`, `*secret*`, `*password*`, `*api_key*`, `*apikey*`. Matched values must be replaced with the literal string `[REDACTED]`; the property name must be preserved so log entries remain parseable. The policy must be registered before any provider, enricher, or sink is attached to the `LoggerConfiguration` so that it applies to every enrichment and sink in the pipeline, including OTLP export. This requirement exists because the FYERS user token is fetched in the browser tier and may traverse request context objects, HttpClient instances, or middleware that would otherwise log it verbatim. The policy must be covered by a unit test that asserts a `LogEvent` containing a property named `access_token` (or any alias in the pattern list) is written with value `[REDACTED]` to the in-memory sink.

## Design System Standards

The design system is the single source of truth for all UI work in `apps/web`. Every visual decision must derive from files under `design_system/`.

### Source of truth files (load order)

1. `design_system/colors_and_type.css` — canonical CSS variables and type scale
2. `design_system/mock_screens/tokens.css` — identical token set used by mock screens
3. `design_system/ui_kits/user-portal/Primitives.jsx` — shared components (Icon, Logo, Pill, Btn, Card, Num, Label, LegalFooter)
4. `design_system/ui_kits/user-portal/AppShell.jsx` — 3-zone layout shell
5. `design_system/ui_kits/admin-portal/` — admin-specific components
6. `design_system/preview/` — visual reference HTML files for every token category
7. `design_system/mock_screens/` — full-page mockups for auth, user, and admin flows

### Mandatory rules for all UI work in apps/web

1. **No custom styling.** Do not introduce new colors, spacing, font sizes, or shadows. Reuse tokens defined in `globals.css` (which mirrors `colors_and_type.css`).
2. **Component reuse first.** Before creating any new component, check `src/components/primitives.tsx`. If a similar component exists, reuse or extend it.
3. **Design fidelity.** Use `design_system/mock_screens/` as the reference for layout and structure. Match spacing, alignment, and hierarchy precisely.
4. **Token usage.** Prefer design tokens over hardcoded values. For example: `padding: var(--s-4)` not `padding: 16px`. `color: var(--fg-2)` not `color: #666`.
5. **Typography.** Use only the defined type scale classes (`t-h1` through `t-label-sm`, `t-num-*`, `t-body-*`) from `globals.css`. No arbitrary font sizes or weights.
6. **Colors.** Only use colors from the token system: `--bg-*`, `--fg-*`, `--brand-*`, `--up-*`, `--down-*`, `--warn-*`, `--info-*`, `--neutral-*`, `--line-*`.
7. **New components.** If absolutely necessary, build using existing primitives. Add the new component to `src/components/primitives.tsx`. Ensure consistency with existing patterns.

### Anti-patterns (strictly disallowed)

- Inline styles with arbitrary values (e.g., `color: "#666"`, `padding: "1rem"`)
- Creating duplicate components that already exist in primitives
- Ignoring design tokens in favor of ad-hoc values
- Using external UI libraries that conflict with the design system
- Adding CSS that bypasses the token system

### Implementation workflow

1. Identify relevant mock screen in `design_system/mock_screens/`
2. Map required components from `src/components/primitives.tsx`
3. Apply tokens for spacing, colors, typography
4. Validate against `design_system/preview/` examples
5. Only then implement

A UI task is not complete unless: no hardcoded styles are introduced, all UI elements map to design system components or tokens, and visual output matches mock screens closely.

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
- **RME coverage exception**: the Risk Management Engine (RME) codebase is carved out of the general .NET 70% target and must independently reach a minimum of **95% line coverage** by unit tests; in addition, the RME must meet a dedicated contract-test floor of at least one contract test per documented state-transition (PendingEntry → Open → Suspended → Closed/Rejected and all sub-paths) and per OCC scenario (clean commit, single-retry success, 3-retry exhaustion/freeze); both the 95% unit-test target and the contract-test floor gate Phase 6 acceptance per REQ-NFR-014
- integration tests must cover every critical workflow end-to-end: OAuth signup, FYERS authentication, Signal Subscription creation and pause/resume, EOD Signal Runner full pass against a seeded universe, RME profile evaluation, order placement Phase 1 modal through to Phase 2 callback handling, account reconciliation, manual adjustment, and admin operations including kill-switch activation
- end-to-end tests must exercise at least the critical user journeys end-to-end through a real browser harness (Playwright or equivalent)
- regression tests are mandatory for any defect found in auth, sessions, FYERS integration, market data ingestion, RME, portfolio accounting, permissions, reconciliation, or notification delivery — the regression test must accompany the fix in the same pull request

### Test classes

- unit test calculations, rules, and state transitions
- integration test persistence-backed workflows across MongoDB and PostgreSQL
- end-to-end test the critical user journeys via a real browser harness
- contract test the Market Data Provider abstraction against each concrete provider implementation

### Performance and resilience

- a load test must be executed and recorded before the Phase B transition: simulate the Phase A approved-user ceiling at peak concurrent activity (dashboard load, chart navigation, intraday sync, LMDS active) and verify all SLO targets in the table below hold; the concurrent user count for the load test must equal the current value of `sys_config.operations.phase_a.tester_ceiling` at the time the test is run (currently 30 — do not hard-code a fixed number in the test script); load-test results gate the Phase B transition per REQ-LEGAL-005 indirectly through REQ-NFR-014

#### Load-test SLO targets (Phase B gate)

All targets are p95 measured at the tester_ceiling concurrent user count with LMDS running. Measurements must be taken against a production-equivalent environment (Azure-hosted, real MongoDB + PostgreSQL, Redis active).

| Surface | Endpoint / operation | p95 target |
|---|---|---|
| Dashboard page load | `GET /api/v1/portfolio/summary` (full portfolio snapshot) | ≤ 500 ms |
| Positions list | `GET /api/v1/portfolio/holdings` (all non-terminal positions) | ≤ 300 ms |
| Signal advisory | `GET /api/v1/signals/subscriptions` (signal subscription list) | ≤ 200 ms |
| RME event processing | Time from event enqueue to durable risk-state commit (Channel drain) | ≤ 2 s |
| WebSocket push | Time from LMDS tick receipt to client WebSocket frame delivery | ≤ 1 s |

Any target miss must be documented as a finding in `audit_events` with scope, p95 measured value, and remediation action before the Phase B transition is unblocked.
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
- keep GitHub Actions workflows reproducible and environment-specific
- keep Azure secrets and environment configuration out of repository code

### Data Recovery Targets (RTO / RPO)

Recovery targets are differentiated by datastore role and time-of-day. "Market hours" is defined as 09:00–15:30 IST on NSE trading days per the internal trading calendar.

| Datastore | Window | RPO (max data loss) | RTO (max downtime) |
|---|---|---|---|
| MongoDB (positions, intent ledger, rme_incidents, trade ledger) | Market hours | ≤ 1 minute | ≤ 15 minutes |
| MongoDB (positions, intent ledger, rme_incidents, trade ledger) | Off-hours / weekends | ≤ 4 hours | ≤ 1 hour |
| PostgreSQL (historical OHLCV) | Any | ≤ 24 hours | ≤ 4 hours |
| Redis (session cache, singleton lease, throttle state) | Any | No durability requirement — Redis is a rebuildable cache; loss is operationally recoverable | ≤ 15 minutes |

These targets define the Azure backup and geo-replication configuration minimums. The MongoDB market-hours targets require continuous backup or change-stream-based replication to a secondary; a daily snapshot alone is insufficient. PostgreSQL historical data is fully rebuildable from the MDP via HDS and DS, so a daily Azure Backup snapshot is sufficient. Recovery exercises validating these targets must be included in the Phase C chaos exercises.

## Caching Conventions

### Shared read-model cache keys

When a single server-side computation is used as the source of truth for multiple UI surfaces, a single shared cache key must be used — not one key per surface. Divergent keys produce stale-vs-fresh inconsistencies visible to the user within the same page load.

**Canonical example — watchlist/chart signal parity (REQ-WATCH-002a):**
The watchlist signal-summary panel and the chart signal overlay both derive their signal state from the same position snapshot. The Worker Service writes the snapshot and stamps it with `ledger_snapshot_version`. Both the watchlist API endpoint and the chart-data endpoint must read from a single Redis cache key keyed on `ledger_snapshot_version`. Neither endpoint may maintain its own separate cache entry for the same underlying data. On each Worker write, the old key is invalidated and the new key is written atomically.

**Rule:** when two or more read paths share a common upstream write, identify the authoritative version stamp on that write (e.g., `ledger_snapshot_version`, `signal_subscription_version_id`) and use it as the shared cache key discriminator. Cache invalidation must occur at write time in the producing service — consumer services must not manage their own TTL-based invalidation for shared data.

## Documentation Standards

- update [requirements-spec.md](./requirements-spec.md) first when product behavior changes
- update architecture docs only for design changes
- update roadmap only for sequencing changes
- keep skill files short and derived from canonical docs
- record meaningful trade-off decisions in `docs/adr`
