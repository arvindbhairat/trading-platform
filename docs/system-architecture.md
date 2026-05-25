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

ASP.NET Core application (thin presentation layer) responsible for:

- auth and session handling
- API endpoints and middleware
- orchestration across MongoDB and PostgreSQL (API-specific flows)
- audit and permission checks at the endpoint layer

Domain logic, storage implementations, shared services (OHLCV, signal evaluation, notifications, market data) live in `packages/` and are shared with the Worker.

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
- applications export OTLP/HTTP directly to the configured vendor endpoint, passing the API key as a header
- no intermediate collector or gateway is deployed (accepted trade-off for solo-POC scope; can be reintroduced later)

### Universe and Reference Data Boundary

- MongoDB stores the symbol master, universe state, archive state, scan-exclusion state, and `sql_table_name_suffix`
- PostgreSQL stores shared historical OHLCV data only
- archive and scan-exclusion are metadata decisions; they do not remove historical availability

### Portfolio Accounting Boundary

- broker holdings and positions are reference inputs
- the platform's internal trade ledger is the accounting source of truth
- FIFO and gross PnL are computed from internal ledger state
- manual adjustments are explicit ledger-side corrections tied to a holding context, never silent rewrites of broker history

## Deployment Topology

The platform is hosted on Azure. The API service and the Worker Service have different deployment shapes because they have different concurrency semantics.

### API service

The API service is stateless and holds no per-user in-memory state that must be serialised. It may run on any number of App Service instances and Azure App Service auto-scale is permitted. All inter-instance coordination happens through MongoDB, PostgreSQL, and Redis.

### Worker Service — single-instance invariant

The Worker Service must run as exactly one instance at any time through Phase A and Phase B. This is load-bearing for RME correctness: ADR-0003 serialises all RME event processing through an in-process `PositionChannelRegistry` — a `ConcurrentDictionary<Guid, Channel<RmeEvent>>` keyed by position ID. A second Worker instance maintains its own channel registry, and events for the same position can then be processed concurrently on different instances. The outcome is silent and catastrophic: lost advisory-flag updates, incoherent state-plus-flag combinations, and duplicate notification writes. The defect cannot be detected by unit tests; it appears only under real market-hours load.

REQ-RME-CONC-006 codifies this invariant as a formal requirement and specifies three enforcement layers.

**Layer 1 — infrastructure as code.** The Worker's App Service plan Bicep or Terraform template sets `workerCount = 1` and declares no auto-scale rule. The deployment pipeline includes a preflight check that fails the release if the target plan's current instance count is not 1 or if any auto-scale rule is attached. This layer prevents accidental scale-out at deploy time.

**Layer 2 — pipeline gate.** The GitHub Actions deployment workflow asserts the absence of auto-scale configuration against the Worker's App Service plan before releasing. A violation fails the build.

**Layer 3 — runtime self-election.** At startup the Worker attempts to acquire a Redis-backed singleton lease keyed `rme:worker:singleton` with a TTL read from `sys_config.operations.worker.singleton_lease_ttl_seconds` (default 60 s), refreshing every 20 s. If a second Worker instance finds the lease held by another, it logs a `singleton_violation` structured error at Error level, writes a `position_concurrency_alert` admin advisory, and exits with a non-zero status code. App Service will restart the process, producing a visible crash-loop signal for operators rather than a hidden degraded state.

If Redis is unreachable at Worker startup, the Worker proceeds to start — consistent with ADR-0003's stance that Redis is not a correctness dependency at runtime — but logs a `singleton_lease_unavailable` warning so the missing enforcement layer is observable. Redis is therefore a visibility dependency for this layer, not a correctness dependency.

### Phase C exit path

Scaling the Worker Service beyond one instance requires implementing the partitioning extension specified in ADR-0003 § Phase C Scaling Extension — Redis-backed partition claims, per-instance channel registries constrained to claimed positions, and heartbeat-driven redistribution on instance failure. A new ADR must document the Phase C partitioning design and approval before Layer 1 and Layer 2 IaC constraints are relaxed.

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
When a logged-in user views live quotes or real-time chart updates in the portal, the platform fetches that data using the user's own FYERS token. This distributes API load across individual user rate limits rather than routing all portal requests through the admin provider. Historical OHLCV data for charts is served from PostgreSQL; only live and intraday price updates are fetched via the user token at display time.

**User Account Data Provider (always FYERS)**
Fetches user-specific account data — positions, orders, trades, and profile — using the individual user's FYERS token. This is not configurable; FYERS is the sole broker integration for account data in the current platform.

**Backend access is REST-only (REQ-MARKET-002a / REQ-MARKET-002b)**
All three of the above provider concerns — shared MDP ingestion, Portal Live Data, and User Account Data — are accessed from backend services (API and Worker) exclusively via the provider's REST interface. The FYERS Data WebSocket, and any equivalent streaming interface a future provider exposes, is explicitly out of scope for the backend. The browser tier may consume the FYERS Data WebSocket directly using the logged-in user's token for in-browser chart and quote refresh; this stream is never proxied or multiplexed through the API service. This rule exists to keep rate-limit accounting, retry, and observability uniform across provider adapters and to keep the provider-abstraction surface narrow.

**Provider-selection note — FYERS is a testing/evaluation choice only**
FYERS was selected as the initial market data provider because its APIs are free of cost during the testing and evaluation phase. The platform is designed to migrate to a commercial third-party data provider (TrueData, Global Data Feeds, or equivalent) ahead of broader rollout. The Market Data Provider abstraction exists precisely to isolate FYERS-specific quirks — rate-limit accounting semantics, daily admin token renewal, bulk-quote batch sizes, per-second pacing — so migration is a configuration and adapter change, not a domain-code change. Reviewers and implementers encountering FYERS-specific assumptions in backend code should treat them as adapter-layer concerns, not platform-level facts. This mirrors REQ-MARKET-002c.

**MDP failure mode — asymmetric degradation on shared-ingestion token loss (REQ-MARKET-016)**
When the admin FYERS token (the shared-ingestion credential used by LMDS and DataSync) is unavailable or fails to refresh, the platform enters an asymmetric degraded state. Components that rely on the *shared-ingestion token* — LMDS, DataSync, and EODSR — suspend. Components that rely on *individual user tokens* — LADS, Portal Live Data (PLD), user chart quotes, RME exit advisories, and the Notification Delivery Job — continue unaffected. The practical effect: users can see their own live prices, view charts, and initiate exits; but inbound entry-signal generation and shared stop-level monitoring are suspended until the admin re-establishes the shared token. This asymmetry is by design — it preserves user visibility into their positions during a common operational event (admin missed daily token renewal) without silently disabling the entire platform. An admin dashboard banner and a one-time Telegram admin alert fire on first token failure. See REQ-MARKET-016 for the full behavioural specification.

### Configuration Layer

Resolves configuration from:

- environment bootstrap values
- Azure App Configuration for shared technical settings
- Azure Key Vault or equivalent secret references
- MongoDB `sys_config` for admin-managed runtime values

### Historical Data Layer

Encapsulates PostgreSQL access for:

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
- post-calendar-edit `time_stop_date` recompute: when the admin saves a calendar change (REQ-CALENDAR-002), a post-commit background job immediately recomputes `time_stop_date` for all non-terminal positions carrying a Time Stop, applies a roll-forward rule for dates that land on newly-introduced holidays, writes per-position audit entries, and emits a single admin summary notification. This job runs inside the Worker Service and is triggered by the API endpoint that saves calendar edits; the API endpoint enqueues a typed event to the Worker rather than running the recompute inline in the HTTP handler, so the HTTP response is not delayed by the recompute. See REQ-STOP-003a.

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

### PostgreSQL

PostgreSQL is split into two logical databases with separate concerns.

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

#### Redis logical database separation (REQ-PORT-031b(c))

Two logical database indexes are required:

| Index | Key pattern | maxmemory-policy | Purpose |
|---|---|---|---|
| `LOCK_DB_INDEX` (default 1) | `ledger:write:lock:*`, `ledger:write:fence:*`, `rme:worker:singleton` | **noeviction** | Coordination locks and fencing-token counters — must never be silently evicted |
| `CACHE_DB_INDEX` (default 0) | all other keys | `allkeys-lru` or similar | Short-lived caches and fan-out — eviction is acceptable |

This separation ensures Redis memory pressure cannot silently evict a held lock or invalidate a fencing-token counter, which would allow concurrent writers to bypass the mutual-exclusion guarantee.  Azure Cache for Redis must enforce these policies in infrastructure-as-code; the separation is documented here as the authoritative reference.

#### Redlock trade-off (REQ-PORT-031b(a))

The initial deployment uses a single Redis node.  Single-node Redlock degrades to a simple `SET NX` with TTL — not safe against Redis node failure while a lock is held (a failover can cause two processes to believe they each hold the lock simultaneously).  This is an accepted trade-off for the Phase A / Phase B testing period.  A three-node Redlock deployment is required before broader rollout.

## Observability Model

- Next.js uses OpenTelemetry instrumentation directly
- ASP.NET Core and .NET worker services use `ILogger` with Serilog plus OpenTelemetry for traces and metrics
- logs, traces, and metrics are exported using OTLP
- logs, traces, and metrics are exported using OTLP/HTTP directly to the configured vendor endpoint
- changing observability vendors requires updating the endpoint URL and API key in configuration

## Key Flows

### HistoricDataSeed Flow

1. Operator triggers the HistoricDataSeed job from the admin portal.
2. Worker loads the list of all active symbols from MongoDB.
3. Worker calls the configured market data provider's historical OHLCV interface, fetching backward from the current date in batches sized to respect the provider's per-call data point limits.
4. Each batch is written to the appropriate `D_` daily PostgreSQL table using the symbol's `sql_table_name_suffix`. After each daily batch is committed, the corresponding `W_` and `M_` candle records for any completed weeks or months within the batch are derived and upserted.
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
4. Latest daily bars are written to PostgreSQL.
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
2. The backtesting engine retrieves historical entry signals for the symbol from PostgreSQL data.
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
9. On stop breach, gap, or circuit event, the RME sets the exit advisory flag on the open position record (per REQ-PLC-002 the position remains in `Open`; advisory conditions are flags, not states) and surfaces an exit advisory.
10. All state transitions are persisted with timestamps and the triggering event reason.

### Chart Decision Flow

1. User opens a chart from the portal or Telegram.
2. API returns historical OHLCV bars from PostgreSQL for the requested timeframe.
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
6. User may adjust quantity and order type (Market or Limit; Limit defaults to RME-suggested price). On every recalculation, the frontend requests a fresh signed order payload from the backend (see REQ-ORDER-009b) and uses it to re-render the `<fyers-button>` element with the backend-signed `data-*` attributes.
7. User clicks the Confirm control, which is a FYERS-branded `<fyers-button>` element populated from the most recent signed payload. Before the FYERS widget activates, the platform writes a matching `intent_ledger` record synchronously. See the FYERS API Connect Branded Button Contract below for the attribute surface, click-capture strategy, callback contract, and reconciliation model.

**Phase 2 — FYERS API Connect widget**

8. The FYERS-hosted pop-up opens pre-populated with the order parameters supplied via the `<fyers-button>` attributes (symbol, product, quantity, order type, price if limit, transaction type BUY or SELL).
9. FYERS handles user auth verification, exchange window checks, input validation, final submission to the exchange, user-facing success and error display, and retry UX entirely within its own pop-up.
10. On completion, the FYERS widget invokes the host-page `finished` callback with signature `function(status, request_token)`. The platform-registered handler is the primary success path: on a success status it updates the matching `intent_ledger` record to `matched`, persists the FYERS-returned identifier (order ID or request token, as confirmed against the FYERS sandbox during Phase 7 contract testing), and the RME transitions the position lifecycle per REQ-PLC-002 (`PendingEntry` → `Open` on fill confirmation from LADS; the `finished` callback alone is not sufficient evidence of fill — it is evidence of submission). On a failure status the handler marks the intent as `submission_failed` and surfaces the error to the user. Handler execution is wrapped in try/catch so that any exception is logged and does not prevent the reconciliation safety net from running.
11. Reconciliation safety net. Independently of the callback, LADS reconciles observed FYERS orders and trades against pending `intent_ledger` records on each cycle. This is the defence-in-depth path that recovers from: the callback never firing (popup blocker, browser closed, network drop between FYERS and the host page); the handler throwing an exception; or the callback arriving but the server-side persist write failing. A successful match via LADS upgrades the intent to `matched` and records that the match source was `lads_reconciliation` rather than `callback`, so operational monitoring can track callback reliability over time.
12. On intent timeout (no callback success and no LADS match within `sys_config.orders.intent_timeout_minutes`): the intent transitions to `unresolved` and an advisory is surfaced to the user asking whether the order was actually submitted.
13. On orphan detection (a LADS-observed order with no matching intent): an orphan advisory is surfaced to the user for acknowledgement and the position lifecycle is updated from the observed trade.

This approach positions every order as a user-initiated, per-order manually confirmed action flowing through FYERS's own hosted infrastructure, which is architecturally distinct from automated or server-side algorithmic order placement. The platform-facing integration contract is defined in the following section.

### FYERS API Connect Branded Button Contract

This section captures the integration surface for FYERS API Connect branded-button mode against which Phase 7 must be built.

**SDK loading.** The SDK script is loaded from `https://api-connect-docs.fyers.in/fyers-lib.js` once per page, placed immediately before the closing `</body>` tag. The SDK URL is pinned in platform configuration. A change-detection step in the deployment pipeline triggers re-execution of the contract test suite before any release that picks up a new SDK revision.

**Custom element.** The SDK registers a `<fyers-button>` custom element. The platform renders one `<fyers-button>` per user-initiated order with order parameters supplied as `data-*` attributes. The element is the direct target of the user click; the platform cannot trigger the widget programmatically.

**Attribute surface.** Confirmed from the FYERS branded-button samples reviewed to date:

- `data-fyers` — platform app-level API key. Client-exposed (not a secret); rotated through the FYERS admin console.
- `data-symbol` — exchange-qualified symbol, format `NSE:{TICKER}-EQ`.
- `data-quantity` — integer, must be positive.
- `data-price` — number. Required for limit orders; non-zero for LIMIT / SL.
- `data-order_type` — enum. `LIMIT` confirmed; full enum (presumed `MARKET`, `SL`, `SL-M`) and per-type required fields such as `data-trigger_price` for SL variants must be confirmed from FYERS docs before Phase 7 build.
- `data-transaction_type` — `BUY` or `SELL`. The platform is long-only per the non-negotiable guardrails in `CLAUDE.md`; `SELL` is only permitted when closing an existing long position (Exit or Reduce actions).
- `data-product` — **always `CNC`** per REQ-ORDER-010. The FYERS branded-button example uses `INTRADAY`; the platform uses `CNC` because the Signal framework produces swing and position trades that must not be auto-squared-off. Confirming that `CNC` is an accepted `data-product` value on `<fyers-button>` is a Phase 7 precondition (see `docs/implementation-roadmap.md`).
- `data-disclosed_quantity` — integer. Confirmed on the custom-button sample; presumed to apply to branded buttons and TBD for confirmation.
- `data-validity` — TBD. DAY and IOC are presumed candidates.
- Correlation or tag attribute for intent-to-order matching — TBD. If the SDK does not expose one, the LADS reconciliation safety-net path falls back to heuristic matching on (user, symbol, side, quantity, price, timestamp window), which is fragile when a user places two similar orders in quick succession. The callback path (below) is unaffected by this limitation because the `finished` callback carries a `request_token` tied to the specific submission.

**Host-side callback (primary success path).** On completion, the widget invokes a host-page handler with signature `function(status, request_token)`. The platform registers a single global handler on SDK load that routes the callback to the open intent for the current user (identified by the pending `intent_ledger` row written at click time, tiebroken by the most-recent `created_at`). On success the handler:

1. Marks the intent `matched` and records `match_source = "callback"`, `request_token`, and `callback_received_at`.
2. Persists a notification in `notifications` for the audit feed.
3. Does **not** transition the position to `Open` on its own — per REQ-PLC-002 the `PendingEntry → Open` transition requires confirmed fill evidence from LADS. The callback confirms submission, not fill.

On failure status the handler marks the intent `submission_failed` with the reported reason and surfaces the error to the user.

The handler is wrapped in a top-level try/catch; any exception is logged via the standard observability stack and deliberately does not prevent the reconciliation safety net from running on the same intent.

The precise `status` enum values, the payload of `request_token`, and whether the order ID is delivered directly or must be resolved via a subsequent read are TBD and must be confirmed against the FYERS sandbox during Phase 7 contract testing. Until confirmed, the handler treats any non-"success" status as failure and always defers the `Open` transition to LADS.

**Intent ledger (defence-in-depth).** The platform persists a MongoDB `intent_ledger` record the moment the user triggers the branded button and before the FYERS widget activates. The record carries: `user_id`, `position_id` (when applicable), `signal_id` (when applicable), `action` (Entry / Add / Reduce / Exit), `symbol`, `side`, `quantity`, `order_type`, `price`, `product`, `created_at`, `status` (`pending` / `matched` / `submission_failed` / `unresolved` / `orphan_ack`), `match_source` (`callback` / `lads_reconciliation`), `request_token`, `matched_broker_order_id`, `callback_received_at`, `reconciled_at`. The collection is specified in full in `docs/data-management.md`.

**Intent capture strategy.** The platform guarantees the intent is persisted before FYERS takes over. Two implementation options satisfy this guarantee:

- *Capture-phase click handler.* The platform registers a click listener on the `<fyers-button>` in the DOM capturing phase and writes the intent synchronously before the event reaches FYERS's own handler. This preserves a single-click UX at the cost of ordering complexity and dependence on browser capturing semantics.
- *Two-step confirm.* Phase 1's modal presents a platform Confirm control that, on click, writes the intent and then reveals the `<fyers-button>` inline in the modal for the user to click. This trades an extra click for deterministic ordering and observability on the write.

Phase 7 selects one strategy and documents the choice here. Either strategy must be exercised by a contract test that verifies no widget activation path bypasses the intent write.

**Signed payload.** Per REQ-ORDER-009b, all `data-*` attribute values rendered onto the `<fyers-button>` must originate from a single signed payload returned by the backend. The backend validates every field before signing — symbol is in the active Nifty 500 universe, `data-product` is `CNC`, quantity is a positive integer, price is non-zero for LIMIT, `data-transaction_type` is consistent with the action (`BUY` for Entry and Add; `SELL` only for Reduce and Exit against an existing long position), and the user is not hard-blocked. The payload is signed with a short-lived HMAC key and bound to the user's session, the action type, and a server-issued nonce; the backend rejects intent-ledger writes whose payload signature fails or whose nonce has been used. Any frontend mutation of `data-*` values between the signing response and the widget click invalidates the signature and the intent write is refused.

**Reconciliation model (safety net).** LADS reconciliation runs on every cycle regardless of whether any callbacks have been received. For each `pending` intent LADS looks for a matching FYERS-observed order. On a match with `match_source` already set to `callback` the reconciliation simply records `reconciled_at` (callback already handled the primary state change) — this gives operational monitoring a reliable "callback matched reality" signal. On a match where the intent is still `pending` (callback never arrived) the reconciliation is the primary path: intent → `matched`, `match_source = "lads_reconciliation"`, the user advisory path surfaces success, and the position lifecycle is driven from the observed trade. Unmatched intents past `sys_config.orders.intent_timeout_minutes` (default 10) transition to `unresolved` with a user advisory asking whether the order was actually submitted. LADS-observed orders without a matching intent transition to `orphan_ack` with a user advisory for acknowledgement. All such advisories are written to the `notifications` collection through the normal Notification Store path.

**Callback reliability monitoring.** The platform emits OpenTelemetry metrics for: `order_callback_received_total`, `order_reconciled_via_lads_only_total` (callback never fired but LADS found the order), `order_unresolved_total`, `order_orphan_total`. A persistent ratio of LADS-only matches above the alertable threshold (configurable via `sys_config.orders.callback_failure_alert_ratio`) raises an admin advisory so a FYERS-SDK regression in callback delivery is visible within hours rather than waiting for a user complaint.

**User-token handoff.** The FYERS intro text indicates the host can start an API session using the user token "once the flow is initiated." The handoff mechanism (redirect URL, `postMessage`, cookie, or a separate OAuth exchange) is not documented in the samples reviewed and is TBD. Until confirmed, the platform continues to obtain user FYERS tokens exclusively through the existing OAuth flow described in the Session and FYERS Lock Flow and does not depend on the widget for token acquisition or refresh.

**SDK version pinning and contract tests.** The SDK URL and any detected payload hash are recorded with each deployment. Phase 7 includes a contract test suite, run against the FYERS sandbox before release and on every detected SDK URL change, covering at minimum: (1) a successful order submission invokes the `finished` callback with a success status and a `request_token` shape the platform recognises; (2) a successful order submission produces an intent that matches a LADS-observed order within the timeout window, with `match_source = "callback"` when the callback fired and `match_source = "lads_reconciliation"` when it did not; (3) an intent created without a subsequent order submission times out to `unresolved`; (4) a LADS-observed order with no matching intent surfaces as an `orphan_ack` advisory; (5) an intentionally-thrown exception inside the callback handler does not prevent the reconciliation safety-net from completing the intent; (6) `data-product = CNC` is accepted by the widget for a representative Nifty 500 symbol.

**Precondition failure path.** The Phase 7 architecture described above is load-bearing on the CNC-on-`<fyers-button>` precondition confirmed in REQ-ORDER-010b. If that sandbox verification fails, the entire branded-button integration surface is not built for V1 and the platform operates in the REQ-ORDER-010a fallback configuration: every surface that would have invoked the Phase 1 platform modal plus the Phase 2 FYERS widget instead presents the REQ-ORDER-017 / 017a handoff UX (clear message plus a symbol-contextualised FYERS deep link). Two architectural constraints remain absolute in the fallback: the platform backend still must not call the FYERS order-placement REST API directly (REQ-ORDER-006) — a REST bypass is not an alternative to the widget — and `INTRADAY` product type must not be substituted for `CNC` (REQ-ORDER-010) because INTRADAY's EOD auto-square semantic is incompatible with the Signal framework's swing and position trades. Platform-native execution assistance is moved to REQ-NEXT-011 for post-V1 investigation of alternative non-REST, non-widget paths such as a FYERS-hosted deep-link order ticket. The branded-button contract section above applies only to the V1-with-Phase-7 configuration; all other sections of this document (RME, lifecycle, notifications, analytics, LADS, LMDS) apply identically in both configurations because the fallback only replaces the in-portal submission path.

## Worker Service Schedule

The Worker Service hosts multiple components that run on different cadences. Their cohabitation within the single Worker process is governed by the following schedule. All times are IST (UTC+5:30) and refer to NSE trading days per the internal trading calendar.

### Pre-session setup (before 09:15 IST on trading days)

The EOD trailing-stop update job runs **synchronously before the LMDS and LADS pollers start** for the new session. This is the ADR-0003 carve-out: `TrailingStopUpdateEvent` is not routed through the per-position Channel — the job applies its recalculation directly to each position document under OCC write guard, guaranteeing that the first LMDS tick of the new session evaluates against an already-updated stop level. The channel consumer must reject any `TrailingStopUpdateEvent` that arrives on the channel and log it as an error.

The Symbol Validity Probe runs once per trading day, triggered by the first successful admin FYERS token generation of the day. It does not block LMDS or LADS startup.

### Market hours (09:15–15:30 IST, Mon–Fri, trading days only)

| Component | Cadence | Notes |
|---|---|---|
| Live Market Data Scan (LMDS) | Continuous; polls at `jobs.live_market_scan.poll_interval_seconds` (default 90 s) | Shared admin token; evaluates stop, add, and reduce levels for all open positions across all users |
| Live Account Data Scan (LADS) | `jobs.account_sync.intraday_interval_minutes` (default 15 min) | Per-user FYERS token; fetches orders, trades, positions, holdings |
| Notification Delivery Job | Continuous; polls for pending notification records on a short interval | Sole dispatcher for Telegram delivery; runs throughout the full trading day and beyond |
| Intent reconciliation (part of LADS) | Every LADS cycle | Matches `intent_ledger` pending records against observed FYERS orders; transitions stale intents to `unresolved` |

LMDS and LADS run simultaneously during market hours. Their poll cycles can overlap; ADR-0003 guarantees that overlapping events for the same position are serialised through the per-position Channel before reaching the RME processor.

### Post-market EOD pipeline (after 15:30 IST)

The EOD pipeline runs in strict sequence. Each step is a precondition for the next.

1. **LMDS stops** at market close.
2. **LADS runs one final sync cycle** after market close to capture any fills or position changes from the closing auction, then stops for the day.
3. **DataSync (DS)** runs post-market. The default trigger time is 17:00 IST (configurable via `REQ-CALENDAR-004`). DS fetches the current day's OHLCV bars and writes them to PostgreSQL. On completion it writes a session success marker.
4. **EOD Signal Runner (EODSR)** starts only if the DS session success marker exists. EODSR evaluates active, non-excluded universe members against each live Signal Subscription and writes entry signals to MongoDB.
5. **RME profile optimisation runs** (if scheduled) begin after EODSR completes. These are non-blocking relative to the next session's setup.
6. **Notification Delivery Job** continues running through EOD and delivers any EODSR-generated signal notifications via Telegram.

**LADS and LMDS must not run during the DataSync and EODSR window.** The EOD pipeline assumes a clean read of the day's final OHLCV data; concurrent intraday polling is not meaningful after market close and would consume shared provider quota.

### Weekends and holidays (non-trading days)

LMDS and LADS are inactive. The Notification Delivery Job continues running to deliver any pending records. DataSync, EODSR, and scheduled RME profile optimisation runs do not execute. Admin-triggered operations (HistoricDataSeed, Universe Sync, manual sys_config changes) are available at any time.

### Serialisation rules within the pipeline

- EOD stop-update job → then LMDS/LADS start (strict pre-session ordering per ADR-0003)
- DataSync success marker → EODSR start (strict pipeline gate per REQ-MARKET-007)
- EODSR → RME profile optimisation runs (sequential, not concurrent)
- Notification Delivery Job runs throughout with no sequencing dependency on the above

The Worker Service must never run LMDS or LADS concurrently with the DataSync or EODSR steps on the same trading day.

### Maintenance window (A-14)

Admin-triggered operations — Universe Sync (CSV upload), HistoricDataSeed (HDS), sys_config changes, and manual trade-ledger operations — are available at any time through the admin portal, but they carry interference risk if run during active scheduled jobs. The preferred maintenance windows are:

- **Pre-market (07:00–08:30 IST)** for Universe Sync and small HDS seeds. This window closes before LMDS starts at pre-session setup (08:45 IST) and avoids interfering with live data flows.
- **Post-EODSR (20:00+ IST)** for large HDS seeds and any operations that produce high PostgreSQL write load. EODSR typically completes by 19:30 IST; running large seeds after 20:00 IST avoids contending with the EOD pipeline.

These windows are informational recommendations for the operator, not platform-enforced scheduling constraints. The admin portal must display these preferred windows in the operational status widget alongside any queued or in-progress admin jobs.

---

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
