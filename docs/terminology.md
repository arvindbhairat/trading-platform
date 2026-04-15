# Platform Terminology

## Purpose

This file is the canonical reference for all named system components, their abbreviations, and the precise meaning of key domain terms used across the platform documentation and codebase.

When writing requirements, architecture notes, code comments, or skill files, use the names and abbreviations defined here. Do not introduce synonyms for existing entries.

---

## System Applications

| Application | Description |
|---|---|
| **Web Portal** (`apps/web`) | Unified Next.js portal serving all user and admin experiences |
| **API Service** (`apps/api`) | ASP.NET Core application owning auth, domain logic, orchestration, and provider adapters |
| **Worker Service** (`apps/worker`) | .NET worker application hosting all background jobs |

---

## Background Jobs

All background jobs run inside the Worker Service. Each has a canonical name and abbreviation for use in logs, job run records, admin UI labels, and documentation.

| Canonical Name | Abbreviation | Trigger | Description |
|---|---|---|---|
| **DataSync** | DS | Scheduled post-market | Fetches the current session's daily OHLCV candles from the configured Market Data Provider and writes them to SQL Server. Derives and upserts weekly and monthly candles for any completed periods. Recovers up to 10 sessions of missed data per run. |
| **HistoricDataSeed** | HDS | Operator-triggered or auto-triggered on new symbols | Seeds SQL Server with full historical OHLCV data for one or more symbols. Gap-fill by default; can overwrite from a specified date on operator request. |
| **EOD Signal Runner** | EODSR | Scheduled post-market, after DataSync succeeds | Evaluates all active (non-paused) Signal Subscriptions across all users against the latest SQL Server data. Emits entry signals and writes notifications. Depends on the DataSync success marker. |
| **Live Market Data Scan** | LMDS | Continuous during NSE market hours | Monitors stop, add, and reduce price levels for every open position across all users using the shared Market Data Provider token. Writes alert records to the notifications collection when a level is breached. Never uses individual user FYERS tokens. |
| **Live Account Data Scan** | LADS | Recurring interval during NSE market hours | Fetches orders, trades, positions, and holdings from FYERS per user using each user's own token. Updates portfolio state and notifies the RME when confirmed trades are detected. |
| **Notification Delivery Job** | NDJ | Continuous / event-driven | The sole component responsible for reading pending notification records from MongoDB and dispatching Telegram messages. No other component dispatches to Telegram directly. |

---

## Core Modules

| Canonical Name | Abbreviation | Description |
|---|---|---|
| **Risk Management Engine** | RME | Strategy-independent, pluggable module responsible for all risk calculations across trade, position, portfolio, and account levels. Operates consistently in both live and backtest modes. All RME outputs are advisory. |
| **Market Data Provider** | MDP | Abstract interface layer isolating market data access from specific provider implementations. Concrete providers include FYERS (admin daily token), TrueData, and Global Data Feeds. All background jobs requiring market data call through the MDP; nothing binds directly to a specific provider. |
| **Backtest Engine** | BTE | The .NET module responsible for replaying historical entry signals through RME profile configurations to produce per-trade, per-position, and portfolio-level performance records. Stores results in the SQL Server backtest database. Uses the same RME implementation as the live signal workflow with no divergence. |

---

## Data Access Patterns

Three distinct data access concerns exist in the platform. Each has its own token context, rate-limit profile, and failure mode. They must never be conflated.

| Canonical Name | Abbreviation | Description |
|---|---|---|
| **Market Data Provider** | MDP | See Core Modules. Configurable shared abstraction for background job market data ingestion using the admin FYERS token (or an alternative configured provider). Never uses individual user tokens. |
| **Portal Live Data** | PLD | Fetches live quotes and real-time OHLCV updates for display in the portal using the individual logged-in user's own FYERS token. Distributes API load across user-scoped rate limits. Historical OHLCV for charts is served from SQL Server; only live and intraday price updates are fetched via this pattern. |
| **User Account Data** | UAD | Fetches user-specific account state — orders, trades, positions, holdings, and profile — from FYERS using the individual user's own FYERS token. Always FYERS; not configurable. Owned by the Live Account Data Scan (LADS) for background sync and by the API layer for on-demand user requests. |

---

## Architectural Layers

These are internal structural layers within the API Service and Worker Service. They are named here to provide a stable vocabulary for code organisation, namespace decisions, and cross-cutting guardrails. Each layer has a single owning application unless noted.

| Layer Name | Owner | Description |
|---|---|---|
| **Configuration Layer** | API Service, Worker Service (shared) | Resolves configuration from environment bootstrap values, Azure App Configuration, Azure Key Vault, and MongoDB `sys_config`. All applications load config through this layer; no component queries `sys_config` ad hoc from business logic. |
| **Historical Data Layer** | API Service, Worker Service (shared) | Encapsulates all SQL Server access for daily, weekly, and monthly OHLCV data. Provides query patterns for chart data, EOD Signal Runner runs, backtesting, and DataSync writes. |
| **Timeframe and Calendar Layer** | API Service, Worker Service (shared) | Provides the single shared implementation of all timeframe and session boundary logic — daily, weekly, monthly, rolling 3/5/7-day bars, and trading-session-aware calendar boundaries. This implementation must be shared without divergence across charting, backtesting, and the EOD Signal Runner. |
| **Signal Layer** | API Service | Manages Signal Subscription definitions, versioned parameter sets, schedules, run state, backtest run records, RME profile optimisation results, and generated entry signals. Does not contain entry-finding logic — that lives in the deployed Signal type implementations. |
| **Portfolio Analytics Layer** | API Service | Builds portfolio state from synced trades and manual adjustments. Exposes holdings, positions, average buy, realized PnL, unrealized PnL, mismatch state, and adjusted-state flags. Owns the `portfolio_snapshots` and `equity_curve` collections. |

---

## Background Operations

Operations that run in the background but are not standalone scheduled jobs. Listed here to avoid ambiguity with the Background Jobs table above.

| Operation | Trigger | Owner | Description |
|---|---|---|---|
| **Universe Sync** | Admin CSV upload (synchronous API-layer operation) | API Service | Validates the uploaded Nifty 500 CSV, diffs it against the MongoDB symbol master, archives removed symbols, and updates symbol metadata. Not a worker job. If net-new symbols result from the upload, the API layer auto-triggers a scoped HistoricDataSeed (HDS) worker job for those symbols before they become eligible for backtesting or EOD scans. |

---

## Domain Concepts

### Signal

A **Signal** is a developer-coded post-market scanner that reads historical OHLCV data, evaluates entry conditions, and emits an entry signal record when those conditions are met. Examples: Volume Spike Signal, Price Volatility Signal.

Key properties of a Signal:
- Logic is authored by developers and deployed as a coded implementation. Users cannot modify the logic.
- Runs as part of the EOD Signal Runner after market close.
- Emits an **entry signal** record (symbol, suggested entry price, ATR, indicator values) when entry conditions are satisfied.
- Does not manage position lifecycle or risk. Everything after entry signal emission is owned by the RME.
- Each Signal type carries a stable string identifier that is persisted on Signal Subscription records, entry signal records, and backtest run records. This identifier must never be reused for a different Signal type.

> **Signal vs Entry Signal**: "Signal" (capital S) refers to the coded scanner type or its Subscription. "entry signal" (lowercase) refers to the output record emitted when a Signal type fires — the data object containing the symbol, entry price, and indicator snapshot that the RME receives as input.

### Signal Subscription

A **Signal Subscription** is a user's configured instance of a Signal type. The user selects a Signal type, sets parameters within the bounds the Signal type exposes, chooses a timeframe, and attaches an RME profile. The Signal Builder is the UI for creating and managing Signal Subscriptions.

A Signal Subscription:
- Belongs to one user and references one Signal type.
- Carries versioned parameter sets and a versioned RME configuration (stop type, sizing model, add/reduce rules, lifecycle parameters).
- Can be paused without deletion.
- Is evaluated by the EOD Signal Runner on each eligible post-market run.

### Entry Signal

An **entry signal** (always lowercase) is the output record produced when a Signal type's entry conditions are satisfied during an EOD Signal Runner run. It contains: symbol, scan type identifier, timeframe, suggested entry price, indicator values at the time of evaluation, ATR, signal timestamp, and processing state. It is the input the RME receives to begin position lifecycle management.

### Other Persistent Concepts

| Term | Description |
|---|---|
| **Symbol Master** | MongoDB store of all Nifty 500 symbol definitions: ISIN, trading symbol, company name, industry classification, archive state, scan-exclusion state, and `sql_table_name_suffix`. |
| **Trade Ledger** | Immutable, append-only MongoDB record of all FYERS-synced trades. The accounting source of truth for FIFO and PnL calculations. |
| **Portfolio Snapshot** | Single per-user MongoDB document updated in place after every account sync. Serves as a display cache for fast dashboard loads. Does not retain history. |
| **Equity Curve** | Append-only per-user time series in MongoDB with one record per completed trading session. The authoritative source for drawdown tracking, equity-curve-based sizing, and historical performance visualisation. |
| **Notification Store** | The `notifications` MongoDB collection. All platform-generated alerts are written here before any delivery is attempted. The portal notification feed reads from this store. |

---

## Naming Notes for Developers

- REQ ID prefixes are stable once assigned and do not change when component names evolve. The `REQ-STRAT-` prefix covers Signal and Signal Subscription requirements as originally assigned.
- The term **scan exclusion** (lower case, hyphenated as a compound modifier) refers to the universe management feature that temporarily excludes an active symbol from being evaluated by the EOD Signal Runner. This is a separate concept from the Signal domain — it is an attribute of the symbol, not of the Signal type.
- The term **strategy-independent** used to describe the RME reflects the architectural boundary: the RME does not contain entry-finding logic. In updated documentation this reads as **Signal-independent**.
- MongoDB collection names: `signals` (Signal Subscription definitions), `signal_runs` (EOD Signal Runner execution records).
