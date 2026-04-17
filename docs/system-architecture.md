# System Architecture

## Purpose

This document explains how the platform is structured to satisfy the canonical requirements in [requirements-spec.md](./requirements-spec.md).
It is an architecture explanation, not the source of truth for product rules.

## Architectural Style

Start with a modular monolith deployed on Azure.
Keep clear internal boundaries so the system can evolve without turning the codebase into a tangle of feature-specific shortcuts.

## Primary Applications

### `apps/web`

Unified Next.js portal for:

- user dashboards
- charting
- strategy configuration
- backtesting views
- portfolio analytics
- admin screens

### `apps/api`

ASP.NET Core application responsible for:

- auth and session handling
- domain logic
- orchestration across MongoDB and SQL Server
- provider adapters
- audit and permission checks

### `apps/worker`

.NET worker application(s) for:

- DataSync (DS)
- HistoricDataSeed (HDS)
- EOD Signal Runner (EODSR)
- Live Market Data Scan (intraday price level monitoring across all positions, shared provider token)
- Live Account Data Scan (per-user FYERS token, fetches orders, trades, positions, holdings)
- Notification Delivery Job (sole dispatcher for all Telegram delivery)
- reconciliation and retry flows

## Core Architectural Boundaries

### Identity and Integration Boundary

- portal OAuth establishes the internal user identity
- FYERS connection is attached only after OAuth identity exists
- admin FYERS auth is reserved for shared market-data and EOD-sync workflows
- user FYERS auth is reserved for user-specific account data
- dirty-token state is persisted and blocks dependent workflows until re-auth succeeds

### Configuration Boundary

- startup-critical config comes from environment variables, Azure App Configuration, and secrets providers such as Azure Key Vault
- MongoDB `sys_config` stores admin-managed runtime settings that should be editable through the portal
- seeded admin email and database connection strings remain outside MongoDB
- applications must be able to start basic logging and config loading before MongoDB-dependent runtime settings are fetched

### Observability Boundary

- .NET services log through `ILogger` with Serilog as the provider
- all applications emit telemetry through OpenTelemetry and OTLP
- applications send telemetry to a stable OTLP collector or gateway endpoint rather than binding directly to one vendor
- vendor-specific routing belongs in collector or gateway configuration, not application code

### Universe and Reference Data Boundary

- MongoDB stores the symbol master, universe state, archive state, scan-exclusion state, and `sql_table_name_suffix`
- SQL Server stores shared historical OHLCV data only
- archive and scan-exclusion are metadata decisions; they do not remove historical availability

### Portfolio Accounting Boundary

- broker holdings and positions are reference inputs
- the platform's internal trade ledger is the accounting source of truth
- FIFO and gross PnL are computed from internal ledger state
- manual adjustments are explicit ledger-side corrections tied to a holding context, never silent rewrites of broker history

## Main Components

### Web Portal Layer

Renders user and admin experiences, including:

- user dashboard home page: portfolio summary, XIRR, period performance, sector breakdown, positions summary, and RME health strip
- chart pages with TradingView Lightweight Charts and full RME advisory panel
- position context: RME advisory state, stop level, add and reduce levels, current R multiple, and portfolio impact breakdown
- archived and scan-exclusion status indicators
- strategy builder and opportunity scan configuration screens
- backtest screens including RME profile comparison and symbol-level recommendations
- admin job controls

### API Layer

Owns validation, permissions, business rules, and read-model assembly.
This layer should keep external payload shapes out of the core domain model.

### Market Data Provider (MDP)

Encapsulates all external provider integrations. Adapters normalise provider-specific payloads before they touch domain services. Two distinct provider concerns must be kept separate:

**Market Data Provider (configurable — background jobs only)**
An abstract interface for shared historical OHLCV data used by background jobs: DataSync (DS), HistoricDataSeed (HDS), and the EOD Signal Runner (EODSR). Concrete implementations include FYERS (admin daily token), TrueData, and Global Data Feeds. The active implementation is selected through admin configuration. All background jobs that need market data call this interface; nothing binds to a specific provider directly. Credentials for third-party providers are stored in Key Vault.

**Portal Live Data (user FYERS token)**
When a logged-in user views live quotes or real-time chart updates in the portal, the platform fetches that data using the user's own FYERS token. This distributes API load across individual user rate limits rather than routing all portal requests through the admin provider. Historical OHLCV data for charts is served from SQL Server; only live and intraday price updates are fetched via the user token at display time.

**User Account Data Provider (always FYERS)**
Fetches user-specific account data — positions, orders, trades, and profile — using the individual user's FYERS token. This is not configurable; FYERS is the sole broker integration for account data in the current platform.

### Configuration Layer

Resolves configuration from:

- environment bootstrap values
- Azure App Configuration for shared technical settings
- Azure Key Vault or equivalent secret references
- MongoDB `sys_config` for admin-managed runtime values

### Historical Data Layer

Encapsulates SQL Server access for:

- daily OHLCV history
- shared timeframe aggregation inputs
- backtests
- EOD Signal Runner runs

### Timeframe and Calendar Layer

Provides one shared implementation for:

- daily, weekly, monthly bars
- rolling 3-day, 5-day, and 7-day bars
- trading-session-aware boundaries
- calendar-aware scheduling

### Signal Layer

Signal types are after-market opportunity scanners. They read historical data, evaluate entry conditions, and emit entry signals containing the symbol, suggested entry price, indicator values, and ATR. They do not own position management or risk decisions.

The Signal layer stores Signal Subscription definitions, versions, parameter sets, schedules, run state, backtest runs, RME profile optimisation results, and generated entry signals.

### Portfolio Analytics Layer

Builds portfolio state from synced trades and manual adjustments, then exposes:

- holdings
- positions
- average buy
- realized PnL
- unrealized PnL
- mismatch state
- adjusted-state flags

Two distinct storage concerns serve this layer:

**`portfolio_snapshots`** — a single document per user updated in place after every account sync and manual refresh. Serves as a display cache for fast dashboard page loads. Does not retain history.

**`equity_curve`** — an append-only time series with one record per user per completed trading session written after EOD sync. Stores market value, realised PnL, unrealised PnL, equity high-water mark, and drawdown. Authoritative source for the RME's drawdown tracking, equity-curve-based sizing adjustments, and historical performance visualisation.

### Risk Management Engine (RME)

A strategy-independent, pluggable module that owns all risk calculations across trade, position, portfolio, and account levels.

The RME is event-driven and responds to: entry signals, price updates, stop events, add and reduce level triggers, portfolio heat changes, drawdown threshold breaches, and market events such as gaps and circuit limits.

The RME receives strategy outputs (entry signals, ATR values, indicator readings) as inputs. It does not calculate indicators internally.

Key responsibilities:

- Position sizing using the configured model (Fixed % Risk, ATR-based, Portfolio Heat-based, Drawdown-adjusted)
- Stop loss calculation and trailing stop management across all supported stop types
- Pyramiding and scaling advisory logic including add and reduce level computation
- Portfolio heat calculation and enforcement
- Drawdown tracking from equity high-water mark with graduated response rules
- Position lifecycle state management (PendingEntry through Closed or Suspended)
- R-based performance metric tracking per trade and across the portfolio
- Execution reality modelling for backtesting (slippage, commission, gap fills)

The RME is deterministic: identical inputs must always produce identical outputs. The same RME implementation serves both backtesting and live signal workflows with no divergence.

The RME is reactive: it only responds to events delivered to it by other components. It does not poll independently. Events that trigger RME responses include: entry fills confirmed by the Live Account Data Scan, price level breaches reported by the Live Market Data Scan, and portfolio state changes from the account sync. On each event the RME updates the relevant position record (state transitions, recalculated levels, advisory flags) and may write advisory alert records to the notifications collection for delivery by the Notification Delivery Job.

All RME outputs are advisory. Position size recommendations, stop levels, add and reduce advisories, and exit recommendations all require explicit user-initiated execution.

### Notification Store

All notifications are written to MongoDB at the point of generation before any delivery is attempted. This makes the notification record the source of truth for what was generated, when, and whether it was delivered.

Each record carries the notification type, target user, symbol context, full content, Telegram delivery status, attempt count, and timestamps. The portal notification feed reads directly from this store.

Every alert-generating component — the Live Market Data Scan (LMDS), EOD Signal Runner (EODSR), and the RME — writes to the notifications collection and stops there. None of these components dispatch to Telegram directly. A single dedicated Notification Delivery Job running in the worker is the sole component responsible for reading pending notification records and dispatching Telegram messages, regardless of notification type. Delivery failures are marked on the record and visible in the portal feed.

### Live Market Data Scan

A worker job that runs continuously during NSE market hours. Fetches live prices for all symbols with open positions across all users using the configured shared market data provider (admin FYERS token when FYERS is the active provider). Evaluates current price against each position's active stop level, add levels, and reduce levels. When a level is breached, writes an alert record to the notifications collection and notifies the RME to update the affected position's state and advisory flags. Uses the shared provider token only; never uses individual user FYERS tokens.

### Live Account Data Scan

A worker job that runs on a configurable interval during NSE market hours, once per user with a valid FYERS token. Fetches orders, trades, positions, and holdings from FYERS using each user's own token. Updates account equity, portfolio state, and the portfolio snapshot. When confirmed trades are detected, notifies the RME to recalculate position levels for the affected positions. More aggressive scan frequency than the Live Market Data Scan because load is distributed across individual user rate limits.

### Symbol Validity Probe

A worker task that runs once per trading day, triggered immediately after the first successful admin FYERS token generation of the day. Fetches FYERS LTP for every active Nifty 500 symbol — including scan-excluded symbols — through the admin FYERS token, in batches that respect the configured rate limits. Updates the `symbol_health` collection with per-symbol outcomes. Distinguishes transient failures from FYERS "unknown symbol" errors; only the latter increment `consecutive_failure_count`. When a symbol's count reaches the configured threshold (`operations.universe.symbol_probe_flag_threshold_days`, default 2), the probe adds it to the admin work queue as a rename-or-delisting candidate. The probe and the CSV-based ISIN rename detection in the Universe Sync flow feed the same queue; cases where both fire for the same symbol are tagged `high_confidence`. The probe is disabled via a `sys_config` flag for incident response, with a visible admin banner when paused.

## Primary Data Stores

### MongoDB

Use MongoDB for:

- users and identities
- approval state and admin access
- FYERS token state and connection metadata
- symbol master and universe state
- internal trading calendar
- Signal Subscriptions, Signal runs, and entry signals
- notification records with per-user delivery status and Telegram attempt history
- synced trades, holdings, positions, and portfolio snapshots
- manual adjustment entries
- admin-managed `sys_config` runtime settings
- audit events and job outcomes

### SQL Server

SQL Server is split into two logical databases with separate concerns.

**Market data database** — stores OHLCV history for all active and archived Nifty 500 symbols:

- three tables per symbol: `D_{suffix}` (daily), `W_{suffix}` (weekly), `M_{suffix}` (monthly)
- each table contains `Date`, `Open`, `High`, `Low`, `Close`, `Volume`
- table names are built at runtime from the `sql_table_name_suffix` stored in the MongoDB symbol master
- weekly and monthly candles are pre-computed and stored by DataSync; rolling window candles are derived on demand from the `D_` table
- query patterns needed for EOD Signal Runner runs and chart data serving

**Backtest database** — stores all persisted backtest run data (`BT_`-prefixed tables):

- one run-header record per backtest execution (Signal type, Signal parameter snapshot, RME profile snapshot, timeframe, execution model assumptions, universe, date range)
- per-trade records linked to the run: entry/exit dates and prices, stop at entry, exit reason, R multiple, gross and net PnL
- per-position lifecycle records: add and reduce events, RME state transitions, peak R
- portfolio-level performance per run: equity curve time series, XIRR, Sharpe ratio, max drawdown, win rate, expectancy, profit factor
- the RME profile optimisation workflow reads from this database when ranking profiles; it does not re-run backtests if a valid stored result already exists for the same scan parameters, RME profile, and data range

### Redis

Use Redis where useful for:

- websocket fan-out
- short-lived caches
- queues and coordination locks

## Observability Model

- Next.js uses OpenTelemetry instrumentation directly
- ASP.NET Core and .NET worker services use `ILogger` with Serilog plus OpenTelemetry for traces and metrics
- logs, traces, and metrics are exported using OTLP
- a collector or gateway fans telemetry out to Better Stack, Axiom, Grafana Cloud, or another vendor
- changing observability vendors should require collector or config changes rather than code changes

## Key Flows

### HistoricDataSeed Flow

1. Operator triggers the HistoricDataSeed job from the admin portal.
2. Worker loads the list of all active symbols from MongoDB.
3. Worker calls the configured market data provider's historical OHLCV interface, fetching backward from the current date in batches sized to respect the provider's per-call data point limits.
4. Each batch is written to the appropriate `D_` daily SQL Server table using the symbol's `sql_table_name_suffix`. After each daily batch is committed, the corresponding `W_` and `M_` candle records for any completed weeks or months within the batch are derived and upserted.
5. If the job is interrupted, it resumes from the last successfully written date for each symbol.
6. On completion, the job records the earliest available date per symbol in MongoDB for use by backtest minimum-data validation.

### Universe Sync Flow

1. Admin uploads the Nifty 500 CSV.
2. The API validates structure and filters to `Series = EQ`.
3. A pre-commit diff preview is assembled showing adds, archives, industry reclassifications, and skipped rows. The diff also detects candidate renames by matching rows where a new `Symbol` appears alongside an `ISIN Code` that matches a currently active symbol whose `Symbol` is absent from the upload; these are presented as `old → new` pairs for admin resolution.
4. The admin resolves each rename candidate as either approve (update symbol master in place, preserve `sql_table_name_suffix` and existing `D_/W_/M_` tables, preserve all historical references) or reject (treat as separate archive-and-add, new symbol gets a fresh suffix and tables).
5. After the admin confirms the diff, the symbol master is updated in MongoDB.
6. Symbols absent from the upload that were not resolved as renames are archived, not deleted.
7. Active symbols may also carry a separate scan-exclusion flag.
8. Upload results, rename resolutions, and a pre-upload snapshot for rollback are stored for audit and review.
9. If net-new symbols were added, the platform automatically triggers HistoricDataSeed for those symbols so their full historical OHLCV data is available before they appear in backtest or scan workflows. Approved renames do not trigger HistoricDataSeed because the underlying tables are preserved.

### Symbol Validity Probe Flow

1. Admin generates the daily FYERS token; the first successful generation of the trading day triggers the probe.
2. The worker loads the current active universe from `symbol_master` (including scan-excluded symbols) and partitions it into batches sized by `operations.universe.symbol_probe_batch_size`.
3. For each batch, the worker fetches LTP from FYERS using the admin token, respecting the FYERS rate-limit configuration.
4. Each per-symbol response is classified as Success, Transient failure, or Unknown-symbol failure per REQ-UNIV-021a. Transient failures are retried within the run with exponential back-off; if any retry succeeds the outcome becomes Success.
5. The `symbol_health` record for each symbol is updated in place with the classified outcome. Success resets `consecutive_failure_count` to zero; Unknown-symbol increments it; Transient failures do not change the count.
6. When a symbol's `consecutive_failure_count` reaches `operations.universe.symbol_probe_flag_threshold_days` (default 2), the platform enqueues a rename-or-delisting candidate entry tagged with that symbol's recent failure history. The entry appears in the same admin work queue as candidates raised by the Universe Sync rename-detection step.
7. Admin resolves each flagged entry as Approve rename (in-place update preserving suffix and tables, same path as REQ-UNIV-020), Mark delisting (archive the symbol and notify users with open positions), or Dismiss (audit the decision but leave the count unchanged, so re-flagging continues).
8. The probe run is recorded in `job_runs`; resolutions are written to `audit_events`.

### EOD Sync and Scan Flow

1. Scheduler or admin starts the EOD sync job.
2. Worker loads the admin FYERS token.
3. If admin auth is missing, dirty, or unusable, sync fails and scans do not start.
4. Latest daily bars are written to SQL Server.
5. Symbols with no valid daily bar for that session are skipped for that session only.
6. Worker writes a session success marker when sync is complete.
7. The EOD Signal Runner begins only if that success marker exists.
8. The EOD Signal Runner evaluates only active, non-excluded universe members.

### Session and FYERS Lock Flow

1. User authenticates via OAuth; a portal session is created with a 24-hour expiry.
2. If the user logs in from a second device, the first session is invalidated server-side immediately.
3. On each authenticated request, the API checks session validity and FYERS token state for the user.
4. If the session has expired, the user is redirected to the OAuth login flow.
5. If the FYERS token is dirty, expired, or missing, the API returns a FYERS re-auth required response.
6. The portal intercepts this response and displays a full-screen modal requiring FYERS re-authentication.
7. The modal cannot be dismissed without completing the FYERS re-auth flow successfully.
8. Once re-auth succeeds, the new token is persisted, the dirty state is cleared, and the portal unlocks.

### Shared Configuration Flow

1. Application starts with environment bootstrap values.
2. Application initializes baseline logging early.
3. Application loads shared technical configuration from Azure App Configuration.
4. Application resolves secrets from Key Vault or environment-secured sources.
5. Application loads admin-managed runtime settings from MongoDB `sys_config` when needed.
6. Admin updates mutable runtime settings through the portal instead of editing the database directly.

### Portfolio Sync and Reconciliation Flow

1. User account sync pulls trades, orders, holdings, and positions from FYERS.
2. Records are ingested idempotently into MongoDB.
3. The ledger rebuilds FIFO holding state.
4. Broker-reported holdings are compared with platform-computed holdings.
5. Quantity mismatch is surfaced on the holdings page as `+/- units`.
6. User may trigger refresh, backfill, or, as a last resort, add a manual adjustment.
7. Adjusted holdings remain visibly flagged for later review.

### Notification Generation and Delivery Flow

1. An alert-generating component (Live Market Data Scan (LMDS), EOD Signal Runner (EODSR), or RME) writes a notification record to MongoDB with status pending; this step is synchronous. The generating component does not attempt Telegram delivery and does not wait for it.
2. The dedicated Notification Delivery Job running in the worker continuously polls for pending notification records.
3. The job attempts Telegram delivery to the user's registered bot chat.
4. On success the record is marked delivered with a timestamp.
5. On failure the job retries with exponential back-off up to the configured maximum attempts.
6. If all retries are exhausted the record is marked delivery failed; the alert remains accessible in the portal notification feed.
7. The portal notification feed reflects the current state of all notification records for the user.
8. Failed deliveries are flagged visually; the user can navigate from any notification to the relevant chart or position page.

### RME Profile Optimisation Flow

1. User or scheduler triggers an RME profile optimisation backtest for a symbol.
2. The backtesting engine retrieves historical entry signals for the symbol from SQL Server data.
3. The engine runs each entry signal through multiple RME profile configurations in sequence.
4. Each run applies the profile's stop mechanism, trailing stop rules, level generation rules, and sizing model with configured slippage and commission.
5. Results are collected per profile: R multiples, win rate, expectancy, max drawdown, Sharpe ratio, and profit factor.
6. The engine stores a ranked comparison result and marks the best-performing profile as the recommended configuration for that symbol.
7. The recommendation is surfaced on the strategy configuration screen and at position entry as the suggested default profile.

### RME Signal and Advisory Flow

1. Strategy scan produces an entry signal for a symbol.
2. The RME receives the signal along with ATR, current account equity, available capital, and current portfolio state.
3. The RME checks portfolio heat, drawdown mode, sector exposure, and concentration rules.
4. If blocked, the RME records the rejection reason and no advisory is surfaced.
5. If allowed, the RME calculates position size using the strategy's configured sizing model.
6. The RME computes the initial stop loss, break-even stop level, trailing stop rules, add levels, and reduce levels.
7. The RME creates a position record in PendingEntry state and surfaces the advisory to the user via Telegram and the portal.
8. On price update events, the RME evaluates trailing stop adjustments, add level triggers, reduce level triggers, and drawdown state.
9. On stop breach, gap, or circuit event, the RME transitions the position to ExitPending and surfaces an exit advisory.
10. All state transitions are persisted with timestamps and the triggering event reason.

### Chart Decision Flow

1. User opens a chart from the portal or Telegram.
2. API returns historical OHLCV bars from SQL Server for the requested timeframe.
3. API returns holding, position, trades, orders, trailing stop, add and reduce levels, current R multiple, and symbol state.
4. UI renders the TradingView Lightweight Chart with OHLCV data plus the full RME advisory panel and position context.
5. UI loads TradingView fundamental widget iframes (Financials, Fundamental Data, Company Profile) for the symbol in `NSE:{symbol}` format; these are served directly from TradingView's servers and require no platform API calls. Widget load failures are handled gracefully and do not affect the chart or RME panels.
6. Optional FYERS API Connect execution actions remain user-initiated via the Phase 1 platform modal and Phase 2 FYERS widget.

### Execution Confirmation Flow

Applies to all user-initiated order actions: Entry, Add, Reduce, and Exit. The flow has two phases: a platform-owned Phase 1 that surfaces RME context and safety checks, and a FYERS-owned Phase 2 that handles the actual order submission through the FYERS API Connect JS widget. The platform backend never calls the FYERS order placement REST API directly.

**Phase 1 — Platform modal**

1. User selects an execution action on the position detail page or signal detail page.
2. Portal API performs pre-flight checks: FYERS token validity, duplicate-in-flight detection, hard-block conditions (kill switch active, drawdown block threshold for new entries).
3. If a hard-block applies, the modal opens in a blocked state — reason displayed, confirm button disabled.
4. Otherwise the modal opens showing: action type, symbol, RME-recommended quantity, estimated fill value at last quoted price, RME stop level, and portfolio heat before and after.
5. Non-blocking warnings are surfaced where applicable: price distance from RME-suggested level, post-trade heat approaching limit, drawdown-reduced sizing.
6. User may adjust quantity and order type (Market or Limit; Limit defaults to RME-suggested price). All dependent values recalculate in real time; the rendered `<fyers-button>` element re-renders with updated `data-*` attributes on every recalculation.
7. User clicks the Confirm control, which is a FYERS-branded `<fyers-button>` element pre-populated with the final order parameters. Before the FYERS widget activates, the platform writes a matching `intent_ledger` record synchronously. See the FYERS API Connect Branded Button Contract below for the attribute surface, click-capture strategy, and reconciliation model.

**Phase 2 — FYERS API Connect widget**

8. The FYERS-hosted pop-up opens pre-populated with the order parameters supplied via the `<fyers-button>` attributes (symbol, product, quantity, order type, price if limit, transaction type BUY or SELL).
9. FYERS handles user auth verification, exchange window checks, input validation, final submission to the exchange, user-facing success and error display, and retry UX entirely within its own pop-up.
10. The widget does not call back into the host page. The platform learns of the submission outcome exclusively through the next Local Account Data Scan (LADS) cycle.
11. LADS reconciles observed FYERS orders and trades against pending `intent_ledger` records. On a match: the ledger entry is marked `matched`, the RME position lifecycle transitions as appropriate (PendingEntry → Submitted → Filled → Open, and analogous for Add, Reduce, and Exit), and the outcome is written to the audit log.
12. On intent timeout (no matching order observed within `sys_config.orders.intent_timeout_minutes`): the intent transitions to `unresolved` and an advisory is surfaced to the user asking whether the order was actually submitted.
13. On orphan detection (a LADS-observed order with no matching intent): an orphan advisory is surfaced to the user for acknowledgement and the position lifecycle is updated from the observed trade.

This approach positions every order as a user-initiated, per-order manually confirmed action flowing through FYERS's own hosted infrastructure, which is architecturally distinct from automated or server-side algorithmic order placement. The platform-facing integration contract is defined in the following section.

### FYERS API Connect Branded Button Contract

This section captures the integration surface for FYERS API Connect branded-button mode against which Phase 7 must be built.

**SDK loading.** The SDK script is loaded from `https://api-connect-docs.fyers.in/fyers-lib.js` once per page, placed immediately before the closing `</body>` tag. The SDK URL is pinned in platform configuration. A change-detection step in the deployment pipeline triggers re-execution of the contract test suite before any release that picks up a new SDK revision.

**Custom element.** The SDK registers a `<fyers-button>` custom element. The platform renders one `<fyers-button>` per user-initiated order with order parameters supplied as `data-*` attributes. The element is the direct target of the user click; the platform cannot trigger the widget programmatically.

**Attribute surface.** Confirmed from the FYERS branded-button samples reviewed to date:

- `data-fyers` — platform app-level API key. Client-exposed (not a secret); rotated through the FYERS admin console.
- `data-symbol` — exchange-qualified symbol, format `NSE:{TICKER}-EQ`.
- `data-quantity` — integer.
- `data-price` — number. Required for limit orders.
- `data-order_type` — enum. `LIMIT` confirmed; the full enum (presumed `MARKET`, `SL`, `SL-M`) and per-type required fields such as `data-trigger_price` for SL variants are TBD and must be confirmed from FYERS docs before Phase 7 build.
- `data-transaction_type` — `BUY` or `SELL`.
- `data-product` — enum. `INTRADAY` confirmed; the full enum (presumed `CNC`, `MARGIN`, `CO`, `BO`) and any per-product required fields (for example cover-order stop-loss) are TBD.
- `data-disclosed_quantity` — integer. Confirmed on the custom-button sample; presumed to apply to branded buttons and TBD for confirmation.
- `data-validity` — TBD. DAY and IOC are presumed candidates.
- Correlation or tag attribute for intent-to-order matching — TBD. If the SDK does not expose one, LADS reconciliation must fall back to heuristic matching on (user, symbol, side, quantity, price, timestamp window), which is fragile when a user places two similar orders in quick succession; this fallback must be documented as a known limitation until a correlation attribute is available.

**No host-side callback.** The branded-button widget owns the entire user-facing flow — user authentication, exchange-window checks, validation, submission, success and error display, retry, and redirect back to the originating page. The widget does not call back into the host page. The platform must not wire any state transition or user-facing outcome to a widget-sourced callback.

**Intent ledger.** The platform persists a MongoDB `intent_ledger` record the moment the user triggers the branded button and before the FYERS widget activates. The record carries: `user_id`, `position_id` (when applicable), `signal_id` (when applicable), `action` (Entry / Add / Reduce / Exit), `symbol`, `side`, `quantity`, `order_type`, `price`, `product`, `created_at`, and `status` (`pending` / `matched` / `unresolved` / `orphan_ack`).

**Intent capture strategy.** The platform guarantees the intent is persisted before FYERS takes over. Two implementation options satisfy this guarantee:

- *Capture-phase click handler.* The platform registers a click listener on the `<fyers-button>` in the DOM capturing phase and writes the intent synchronously before the event reaches FYERS's own handler. This preserves a single-click UX at the cost of ordering complexity and dependence on browser capturing semantics.
- *Two-step confirm.* Phase 1's modal presents a platform Confirm control that, on click, writes the intent and then reveals the `<fyers-button>` inline in the modal for the user to click. This trades an extra click for deterministic ordering and observability on the write.

Phase 7 selects one strategy and documents the choice here. Either strategy must be exercised by a contract test that verifies no widget activation path bypasses the intent write.

**Reconciliation model.** Because the widget does not call back, LADS is the sole source of truth for submission outcomes. Each LADS cycle matches FYERS-observed orders and trades against pending `intent_ledger` records for that user. Matches drive RME position-lifecycle transitions; unmatched intents past `sys_config.orders.intent_timeout_minutes` (default 10) transition to `unresolved` with a user advisory; LADS-observed orders without a matching intent transition to `orphan_ack` with a user advisory. All such advisories are written to the notifications collection through the normal Notification Store path.

**User-token handoff.** The FYERS intro text indicates the host can start an API session using the user token "once the flow is initiated." The handoff mechanism (redirect URL, `postMessage`, cookie, or a separate OAuth exchange) is not documented in the samples reviewed and is TBD. Until confirmed, the platform continues to obtain user FYERS tokens exclusively through the existing OAuth flow described in the Session and FYERS Lock Flow and does not depend on the widget for token acquisition or refresh.

**SDK version pinning and contract tests.** The SDK URL and any detected payload hash are recorded with each deployment. Phase 7 includes a contract test suite, run against the FYERS sandbox before release and on every detected SDK URL change, covering at minimum: (1) a successful order submission produces an intent that matches a LADS-observed order within the timeout window, (2) an intent created without a subsequent order submission times out to `unresolved`, (3) a LADS-observed order with no matching intent surfaces as an `orphan_ack` advisory.

## Cross-Cutting Concerns

- auditable admin actions
- idempotent background jobs
- explicit success and failure markers for key jobs
- server-side validation for all sensitive flows
- shared timeframe and calendar semantics across research and live workflows
- clear separation between provider state and internal domain state

## Documentation Boundary

- product requirements live in [requirements-spec.md](./requirements-spec.md)
- delivery sequencing lives in [implementation-roadmap.md](./implementation-roadmap.md)
- build and operational conventions live in [engineering-standards.md](./engineering-standards.md)
- runtime configuration tiers and `sys_config` structure live in [system-config.md](./system-config.md)
