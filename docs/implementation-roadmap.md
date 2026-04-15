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

- embed FYERS API Connect JS SDK (`fyers-lib.js`) in the Next.js portal
- implement pre-flight check API endpoint: FYERS token validity, kill-switch state, drawdown block state, duplicate-in-flight detection
- build Phase 1 platform confirmation modal for Entry, Add, Reduce, and Exit actions: RME advisory state, recommended quantity, stop level, post-trade portfolio heat, Market/Limit order type selection with real-time value recalculation (Limit defaults to RME-suggested price), CNC product type enforced
- implement non-blocking warning layer in Phase 1 modal: price-distance from RME level, heat-approaching-limit, drawdown-adjusted sizing
- on Phase 1 confirm, pre-populate FYERS API Connect widget attributes (symbol, product, quantity, order type, price, transaction type BUY/SELL) and invoke the widget; FYERS-hosted pop-up handles auth, exchange checks, and final submission
- handle the API Connect `finished` callback: record order ID on success, surface FYERS error on failure, write audit log entry in both cases
- queue RME state refresh on next portfolio sync following a confirmed fill

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
