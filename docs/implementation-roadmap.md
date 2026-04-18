# Implementation Roadmap

## Purpose

This roadmap sequences delivery work derived from [requirements-spec.md](./requirements-spec.md).
It must not be used as the place to invent new product requirements.

## Delivery Principle

Ship in thin vertical slices that end in something a user or operator can validate.

## Phase 0: Foundation

- initialize repo structure
- configure local environment, linting, typing, formatting, and tests
- set up Azure DevOps pipelines
- define MongoDB, SQL Server, Redis, and worker runtime patterns
- establish ASP.NET Core backend and .NET worker baselines
- establish OpenTelemetry, OTLP, Serilog, and collector wiring
- establish Azure App Configuration, Key Vault integration, and configuration bootstrap patterns
- wire structured logging and health checks

## Phase 1: Identity and Access

- implement OAuth sign-in
- create user, account, role, and approval models
- implement seeded bootstrap admin behavior
- add FYERS credential management and token lifecycle handling
- add dirty-token and re-auth-required UX
- define and seed the `sys_config` collection schema and initial runtime keys
- build admin management UI for shared runtime `sys_config` settings
- build admin approval flows

## Phase 2: Universe and Market Data

**Preconditions (must be confirmed against the FYERS sandbox before Phase 2 market-data code is written):**

- Bulk Quotes rate-limit accounting: verify whether a single Quotes REST request for up to 50 comma-separated symbols consumes one rate-limit hit or `N` rate-limit hits (where `N` equals the number of symbols in the request). The result determines the safe lower bound on `jobs.live_market_scan.poll_interval_seconds` and the overall LMDS capacity model. The verified outcome must be recorded in `docs/operations/fyers-api-budget.md` § LMDS before any LMDS implementation work starts, and the default `sys_config` seed value must be adjusted if the verified accounting invalidates the current 90 s default. Per REQ-MARKET-002c, this verification is FYERS-specific; when the platform migrates to a third-party market data provider the equivalent check must be performed against that provider's published rate-limit rules and the budget model refreshed accordingly.

**Build work:**

- create symbol master and universe state models
- build admin CSV upload with archive and exclusion handling
- implement internal trading calendar and admin management screens
- implement Market Data Provider (MDP) abstraction layer with common interface
- implement FYERS as the initial concrete MDP implementation
- implement TrueData or Global Data Feeds as a second concrete MDP implementation (stub if not yet contracted)
- add admin config for active MDP selection
- build HistoricDataSeed job: operator-triggered, batched, resumable, fetches backward from current date
- build post-market DataSync job: daily, fetches current day minus 10 sessions through current session as recovery buffer
- build chart-data endpoints and live quote flows routing through provider abstraction
- define SQL Server historical schema and sync patterns

## Phase 3: Strategy Research

- model Signal Subscription definitions, versions, and persisted parameter sets
- implement SQL Server historical query layer
- implement shared timeframe aggregation
- build backtest engine and result storage
- deliver Signal Builder and backtest result screens

## Phase 4: Live Operations

- implement DataSync (DS) EOD job and success-marker handling
- implement EOD Signal Runner (EODSR)
- implement notification store in MongoDB as the primary record of all alerts and advisories
- implement async Telegram delivery worker with retry and back-off; mark delivery status on each record
- add Telegram bot provisioning and user deep-link subscription flow
- add Telegram deep links and brief portfolio impact summary into signal notifications
- build portal notification feed with filtering, unread indicators, and deep links to chart pages
- flag failed Telegram deliveries visibly in the portal feed
- honor universe archive and scan-exclusion rules in live workflows

## Phase 5: Portfolio Analytics

- sync trades, holdings, positions, and orders from FYERS
- build immutable trade ledger
- support first-time backfill and incremental sync
- add reconciliation and mismatch detection
- support audited manual adjustments tied to holdings
- deliver holdings, PnL, and reconciliation views

## Phase 6: Risk Management Engine

- implement the RME as a standalone, strategy-independent module with abstract plugin interfaces
- implement R-based risk unit calculations
- implement position lifecycle state machine (PendingEntry through Closed and Suspended)
- implement pluggable position sizing models: Fixed Percentage Risk Per Trade (default), ATR-based, Portfolio Heat-based, and Drawdown-adjusted
- implement pluggable stop loss mechanisms: Fixed, ATR, Trailing (percentage), Trailing (ATR), Swing Low, and Break-Even
- implement RME profile concept: unified configuration set at position entry, persisted with position record, immutable for lifecycle duration
- implement portfolio heat calculation and enforcement
- implement account-level drawdown tracking from equity high-water mark
- implement graduated drawdown response rules and drawdown mode behaviour
- implement pyramiding and scaling advisory logic with add and reduce level computation
- implement z-score overlay as an add-on advisory signal
- implement exit priority ordering when multiple conditions trigger simultaneously
- implement gap risk and circuit limit detection and advisory surfacing
- implement pre-trade portfolio impact assessment: projected heat, sector exposure, cash reserve, correlation context, and worst-case stop scenario
- implement execution reality modelling for backtesting (slippage, commission, gap fills)
- implement per-trade and portfolio-level risk metrics (MAE, MFE, R multiples, Sharpe, Sortino, etc.)
- implement automated RME profile optimisation backtests: run symbol entry signals through multiple profile configurations, compare outcomes, store and surface ranked recommendations per symbol
- wire RME into strategy backtest engine and live scan workflows using one shared implementation
- surface full RME advisory panel on chart page: entry rationale, stop level, add and reduce levels, current R multiple, portfolio impact breakdown
- surface brief portfolio impact summary in Telegram entry signal notifications
- surface RME advisory state and portfolio health indicators when user browses positions manually
- implement admin global kill switch for platform-wide signal suspension
- implement per-user and per-strategy enable and disable controls
- build user dashboard: portfolio summary, XIRR, period performance (today, rolling 5/15/30 sessions, WTD, MTD), sector breakdown, positions summary with RME advisory state, and RME portfolio health strip

## Phase 7: Execution Assistance

**Preconditions (must be confirmed against the FYERS sandbox before build starts):**

- `CNC` is an accepted `<fyers-button>` `data-product` value for a representative Nifty 500 equity symbol (REQ-ORDER-010). If FYERS rejects `CNC` on the branded button, raise this as a blocker before any Phase 7 code is written — the platform's swing/position-trading semantics cannot be satisfied by `INTRADAY`.
- Exact `status` enum values for the `finished` callback and the payload shape of `request_token` (whether it is the order ID directly or must be exchanged for one).
- Whether the SDK exposes a per-button correlation or tag attribute the platform can use to disambiguate multiple near-simultaneous orders from the same user.

**Build work:**

- embed FYERS API Connect JS SDK (`fyers-lib.js`) in the Next.js portal; pin the SDK URL in platform configuration and wire a deployment-time hash check so SDK changes trigger the contract test suite
- implement pre-flight check API endpoint: FYERS token validity, kill-switch state, drawdown block state, duplicate-in-flight detection
- build Phase 1 platform confirmation modal for Entry, Add, Reduce, and Exit actions: RME advisory state, recommended quantity, stop level, post-trade portfolio heat, Market/Limit order type selection with real-time value recalculation (Limit defaults to RME-suggested price), CNC product type enforced
- implement non-blocking warning layer in Phase 1 modal: price-distance from RME level, heat-approaching-limit, drawdown-adjusted sizing
- implement the signed order payload endpoint (REQ-ORDER-009b): server-side validation of every attribute, HMAC signing bound to session + action + single-use nonce; frontend requests a fresh signed payload on every Phase 1 recalculation and uses it verbatim to populate `<fyers-button>` `data-*` attributes
- provision the `intent_ledger` MongoDB collection per `docs/data-management.md` with the specified indexes (compound `{ user_id, status, created_at }`, unique `nonce`)
- implement the pre-widget intent write (REQ-ORDER-014a) using the chosen capture strategy (capture-phase click handler or two-step confirm); exercise the choice in the contract test suite to prove no widget activation path bypasses the intent write
- on Phase 1 confirm, pre-populate FYERS API Connect widget attributes from the signed payload and invoke the widget; FYERS-hosted pop-up handles auth, exchange checks, and final submission
- register the global `finished` callback handler (REQ-ORDER-015): on success mark intent `matched` with `match_source = "callback"`, record `request_token` and order ID if returned, surface confirmation, write audit log entry; on failure mark intent `submission_failed`, surface FYERS error, write audit log entry; wrap the handler in a top-level exception boundary
- extend LADS with the intent-reconciliation safety net (REQ-ORDER-015c): on every cycle match observed FYERS orders against `pending` intents; close callback-matched intents with `reconciled_at`; promote `pending → matched` with `match_source = "lads_reconciliation"` where callback never arrived; transition stale intents to `unresolved`; raise `orphan_ack` advisories for observed orders with no matching intent
- queue RME state refresh on next portfolio sync following a confirmed fill (PendingEntry → Open is driven by LADS-confirmed fill evidence, not by the callback alone)
- emit callback-reliability OpenTelemetry metrics (REQ-ORDER-015d) and wire the admin alert that fires when the LADS-only match ratio exceeds `orders.callback_failure_alert_ratio`

**Contract test suite (run against the FYERS sandbox before release and on every detected SDK URL change):**

1. successful submission invokes the `finished` callback with a recognised success status and a `request_token`
2. successful submission produces an intent matching a LADS-observed order within the timeout window, with `match_source = "callback"` when the callback fired and `match_source = "lads_reconciliation"` when it did not
3. an intent created without a subsequent order submission times out to `unresolved`
4. a LADS-observed order with no matching intent surfaces as an `orphan_ack` advisory
5. an intentionally-thrown exception inside the callback handler does not prevent reconciliation from completing the intent
6. `data-product = CNC` is accepted by the widget for a representative Nifty 500 symbol
7. signed-payload nonce replay is refused; mutated `data-*` values between signing and click are refused

## Phase 8: Admin Operations and Hardening

- add admin job monitoring and manual retry controls
- add alerting, kill-switches, and operational safeguards
- harden retries, dead-letter handling, and reconciliation jobs
- document incident and recovery workflows

## Phase 9: Corporate Actions and Data Continuity

- add splits, bonuses, dividends, and symbol continuity handling
- decide whether provider data is sufficient or a dedicated source is required
- apply continuity rules across charts, backtests, and holdings analytics

## Ongoing Rules

- start each feature from domain model and interface design
- keep roadmap phases aligned with the canonical requirements spec
- update architecture and standards docs only when their concerns change
