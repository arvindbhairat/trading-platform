# Data Management Reference

## Purpose

This document catalogues all persistent storage used by the platform: MongoDB collections and SQL Server databases. It is the reference for database setup, TTL index configuration, MongoDB Online Archive policies, and SQL Server database provisioning.

This document is a setup and operational reference. Product requirements live in [requirements-spec.md](./requirements-spec.md).

---

## SQL Server Databases

The platform uses two separate SQL Server databases. They must be provisioned separately to isolate backtest write load from market data read performance.

### Market Data Database

Holds pre-computed OHLCV history for all active and archived Nifty 500 symbols. For each symbol there are three tables built from the symbol's `sql_table_name_suffix`:

| Table name pattern | Timeframe | Content |
|---|---|---|
| `D_{suffix}` | Daily | One row per trading session |
| `W_{suffix}` | Weekly | One row per ISO calendar week (pre-computed by DataSync) |
| `M_{suffix}` | Monthly | One row per calendar month (pre-computed by DataSync) |

All three tables share the same schema: `Date`, `Open`, `High`, `Low`, `Close`, `Volume`.

Rolling window candles (3-day, 5-day, 7-day) are not stored here; they are derived on demand from the `D_` table.

**Per-symbol table lifecycle.** The three tables for a symbol are materialised by HistoricDataSeed at the moment the symbol joins the symbol master — either via a CSV upload add (REQ-UNIV-014) or a rename-reject resolution (REQ-UNIV-020(b)). Creation is idempotent and per-symbol-atomic (three-table transaction) per REQ-HIST-009a. Fleet-wide schema evolution — adding a column across every existing symbol's three tables, for example — is a separate admin-triggered batched migration governed by REQ-MIGRATION-004. Both paths must use the same shared DDL template so the schema of an existing symbol's tables and a newly-created symbol's tables cannot drift.

Retention: no expiry. Historical data is append-only and must be available for backtesting regardless of age. Archived symbols continue to receive data while they remain in the Nifty 500 universe (see REQ-UNIV-008).

### Backtest Database

Holds all persisted backtest run output. Tables use a `BT_` prefix. The full schema is defined during implementation but must cover at minimum:

- run header: run ID, Signal type and parameter snapshot, RME profile snapshot, timeframe, slippage and commission assumptions, symbol universe, date range, run timestamp
- per-trade records linked to run ID: symbol, entry/exit dates and prices, stop at entry, exit reason, R multiple, gross and net PnL
- per-position lifecycle records linked to run ID: add/reduce events, RME state transitions, peak R multiple
- portfolio performance per run: equity curve time series, XIRR, Sharpe ratio, max drawdown, win rate, expectancy, profit factor, starting and ending equity

Retention: no expiry. Stored backtest results must remain queryable by the RME profile optimisation workflow at any time without re-running the scan logic.

---

## MongoDB Lifecycle Mechanisms

**TTL Index**: MongoDB natively expires and removes documents after a configured duration from a timestamp field. Suitable for data that genuinely has no value after a fixed window.

**Online Archive**: MongoDB Atlas moves data matching a date-based condition to a lower-cost object storage tier. Documents remain queryable through a federated connection but are no longer in the hot tier. Suitable for data that must be retained long-term but is rarely accessed after its active period.

**No expiry**: Core operational data that must remain in the hot tier indefinitely (small reference collections, current-state records, audit-critical immutable records).

---

## Collections

### `users`

**Purpose**: User accounts, profile data, role assignments, approval state, and Telegram chat configuration.

**Lifecycle**: Permanent. Records are deactivated (soft-delete with status flag), never hard-deleted.

**Mechanism**: No TTL. No archive. Small collection; full retention is required for audit and account recovery.

---

### `sessions`

**Purpose**: Server-side portal OAuth session records. Each record stores user ID, session token, issued-at timestamp, expires-at timestamp, and device or user-agent context. Used to enforce the single-active-session rule (REQ-SESSION-002) and to validate all authenticated API requests.

**Lifecycle**: Automatically expired 24 hours after issuance. On new login the existing session record for the user is invalidated before the new one is written.

**Mechanism**: TTL index on `issued_at`. Expire documents 24 hours after issuance.

**Default expiry**: 24 hours.

---

### `fyers_tokens`

**Purpose**: FYERS OAuth token state per user: access token, refresh token, expiry timestamp, dirty flag, and re-auth metadata. One active record per user plus the admin shared token record.

**Lifecycle**: The current token record is updated in place on each successful re-auth. Superseded token records are retained for a short audit window.

**Mechanism**: TTL index on superseded records. Expire 30 days after the token was replaced (a `superseded_at` timestamp is set on replacement). Active records (no `superseded_at`) are never expired.

**Default expiry**: 30 days after supersession.

---

### `symbol_master`

**Purpose**: Nifty 500 symbol definitions including ISIN, trading symbol, company name, series, industry classification (the `Industry` field from the NSE CSV, used as the platform's sector grouping), archive state, scan-exclusion state, and the stable `sql_table_name_suffix` used to construct the `D_`, `W_`, and `M_` SQL Server table names for that symbol. ISIN is stored alongside symbol as the continuity anchor used by the rename-detection workflow (REQ-UNIV-020) and the Symbol Validity Probe resolution path (REQ-UNIV-021b): when an NSE rebranding changes the trading symbol but leaves the underlying security unchanged, the ISIN match drives an in-place update that preserves `sql_table_name_suffix` and the associated historical tables. Stock splits, bonus issues, and consolidations change the ISIN while leaving the trading symbol unchanged — the symbol master record updates ISIN in place and the suffix remains stable.

**Lifecycle**: Permanent. Symbols are archived in place when removed from the universe; historical records are never deleted because the `sql_table_name_suffix` mapping must remain stable for backtest queries. `sql_table_name_suffix` is immutable once assigned: it never changes for the lifetime of the symbol master record, even across trading symbol renames.

**Mechanism**: No TTL. No archive.

---

### `symbol_health`

**Purpose**: Per-symbol daily health tracking populated by the Symbol Validity Probe (REQ-UNIV-021). One document per active symbol. Fields: `symbol`, `last_successful_probe_at`, `last_unknown_symbol_at`, `consecutive_failure_count`, `last_error_code`, `last_error_at`, `status` (`ok`, `flagged`, `resolved`), and a bounded recent-event history for admin diagnostics. Drives the admin work queue when `consecutive_failure_count` reaches the configured flag threshold; cleared or archived on rename, delisting, or dismissal resolutions per REQ-UNIV-021b.

**Lifecycle**: Updated in place on every probe run. When a symbol is archived via either rename rejection or a "mark delisting" resolution, its `symbol_health` record is retained in a resolved state for audit purposes alongside the archived `symbol_master` entry.

**Mechanism**: No TTL. No archive. Small collection (capped at the active universe size).

---

### `trading_calendar`

**Purpose**: NSE trading session records covering all session types: normal sessions, special sessions, and Muhurat sessions. Each record stores session date, session type, session start time, and session end time in `Asia/Kolkata`. Exchange holidays are represented by the absence of a session record for that date. Used by the timeframe and calendar layer to generate session-aware bar boundaries, scheduler inputs, and market-hours determinations.

**Lifecycle**: Permanent for past sessions. Future sessions are seeded from exchange holiday schedules and corrected by admin as needed.

**Mechanism**: No TTL. No archive. Small collection.

---

### `sys_config`

**Purpose**: Admin-managed runtime configuration values (see [system-config.md](./system-config.md) for the full schema). Includes rate limit thresholds, portfolio heat limits, drawdown thresholds, sizing defaults, and market data provider selection.

**Lifecycle**: Permanent. Values are updated in place through the admin portal. An audit trail of changes is written to `audit_events`.

**Mechanism**: No TTL. No archive.

---

### `watchlists`

**Purpose**: Per-user watchlist of Nifty 500 symbols. Each record stores the user ID and an ordered list of symbol references the user has added. Used by the watchlist page to display price data fetched via the user's FYERS token.

**Lifecycle**: Permanent per active user. One document per user, updated in place as symbols are added or removed.

**Mechanism**: No TTL. No archive.

---

### `signals`

**Purpose**: Signal Subscription definitions: Signal type identifier, name, version history, parameter sets, enabled state, schedule configuration, and per-Signal and per-user enable or disable controls.

**Lifecycle**: Permanent. Strategies are deactivated, not deleted. Versioning is append-only.

**Mechanism**: No TTL. No archive.

---

### `signal_runs`

**Purpose**: EOD Signal Runner execution records: Signal Subscription ID, run timestamp, Signal type, symbols evaluated, entry signals generated, duration, and outcome.

**Lifecycle**: Active for 90 days for operational monitoring and debugging. Older records move to archive tier.

**Mechanism**: Online Archive. Archive condition: `run_at` older than 90 days. Archived data remains queryable via federated connection for historical performance review.

**Default archive threshold**: 90 days.

---

### `entry_signals`

**Purpose**: Entry signals emitted by the EOD Signal Runner (EODSR): symbol, Signal type identifier, timeframe, suggested entry price, indicator values, ATR, signal timestamp, Signal Subscription version, and processing state (pending RME evaluation, accepted, rejected).

**Lifecycle**: Active for 12 months. Older signals move to archive tier for backtest and audit purposes.

**Mechanism**: Online Archive. Archive condition: `generated_at` older than 12 months.

**Default archive threshold**: 12 months.

---

### `notifications`

**Purpose**: All notification records generated by the platform: notification type, target user ID, symbol context, full content payload, Telegram bot ID, Telegram delivery status, attempt count, last attempt timestamp, and error details. The portal notification feed reads from this collection.

**Lifecycle**: Active for 90 days (covers the user-facing notification feed window). Older records move to archive tier for audit.

**Mechanism**: Online Archive. Archive condition: `generated_at` older than 90 days.

**Default archive threshold**: 90 days.

---

### `notification_bots`

**Purpose**: Admin-provisioned Telegram bot configuration. The platform operates with a single shared bot at any one time. Each record stores the bot's verified username, the bot token Key Vault reference (never stored inline), active state, and provisioning metadata including who configured it and when. A new record is written when the admin replaces the bot token per REQ-NOTIFY-015; the previous record is retained for audit purposes with its active flag set to false.

**Lifecycle**: Permanent. Previous bot records are retained for audit; only the active record drives delivery.

**Mechanism**: No TTL. No archive. Small reference collection.

---

### `positions`

**Purpose**: Position records representing the platform's internal model of each user's open and closed positions. Includes entry details, quantity, average buy, RME profile snapshot (immutable for position lifecycle), current RME state (stop level, add and reduce levels, trailing stop state, lifecycle state, current R multiple), and closed outcome data.

**Required fields (schema notes)**: Every position document must carry a `_version` field (long integer, initialised to 1 on creation, incremented on every successful write) to support the optimistic concurrency control mechanism required by `REQ-RME-CONC-002`. All RME writes to this collection must filter on `{ _id, _version }` and check `MatchedCount`. A migration seeding `_version: 0` on any pre-existing documents without this field must run before Phase 6 RME implementation. The first successful RME write upgrades each document to `_version: 1`.

**Lifecycle**: Active while a position is open or recently closed (within 12 months of close date). Older closed positions move to archive tier.

**Mechanism**: Online Archive. Archive condition: `closed_at` present and older than 12 months. Open positions (no `closed_at`) are never archived.

**Default archive threshold**: 12 months after close.

---

### `rme_events`

**Purpose**: Immutable event log for all RME state transitions: position ID, event type (stop adjustment, add level trigger, reduce level trigger, drawdown state change, exit advisory, etc.), triggering price or market event, resulting state, and timestamp.

**Lifecycle**: Active for 24 months (covers the active trading history window). Older events move to archive tier.

**Mechanism**: Online Archive. Archive condition: `event_at` older than 24 months.

**Default archive threshold**: 24 months.

---

### `trade_ledger`

**Purpose**: Immutable append-only records of all trades synced from FYERS: trade ID, symbol, side, quantity, price, trade timestamp, order ID, sync source (FYERS trade history or intraday), and ingestion timestamp. This is the accounting source of truth for FIFO and PnL calculations.

**Lifecycle**: Permanent. Trade records must never be deleted or modified. Older records move to archive tier.

**Mechanism**: Online Archive. Archive condition: `trade_at` older than 36 months. Federated query support ensures backfill and historical PnL calculations can still reach archived records.

**Default archive threshold**: 36 months.

---

### `intent_ledger`

**Purpose**: Per-user order intent records written synchronously at the moment a user triggers a FYERS API Connect branded button, before the FYERS widget activates. Each record is the platform's authoritative pre-submission view of what the user attempted to submit. Two downstream paths update the record: the widget `finished` callback (primary success path, REQ-ORDER-015) and the LADS reconciliation safety net (defence-in-depth, REQ-ORDER-015c). Supports idempotent callback handling, orphan-order detection, unresolved-intent advisories, and operational monitoring of widget callback reliability.

**Required fields**: `user_id`, `position_id` (when the intent references an existing platform position), `signal_id` (when the intent originates from a platform signal advisory), `action` (`Entry` / `Add` / `Reduce` / `Exit`), `symbol`, `side` (`BUY` / `SELL`), `quantity`, `order_type`, `price`, `product` (always `CNC` per REQ-ORDER-010), `status` (`pending` / `matched` / `submission_failed` / `unresolved` / `orphan_ack`), `match_source` (`callback` / `lads_reconciliation` / null for non-terminal states), `request_token` (set on callback), `matched_broker_order_id` (set when the observed FYERS order ID is resolved, either from the callback payload if FYERS returns it there or from LADS reconciliation otherwise), `callback_received_at`, `reconciled_at`, `created_at`, `updated_at`, `nonce` (single-use server-issued nonce that bound the signed order payload per REQ-ORDER-009b; persisted to prevent nonce replay), `payload_signature` (HMAC signature of the signed order payload, retained for audit).

**Indexes**: compound `{ user_id: 1, status: 1, created_at: -1 }` for LADS reconciliation queries per user; unique `{ nonce: 1 }` to enforce single-use nonces; TTL helper index on `created_at` for the archive/ttl mechanism described below.

**Lifecycle**: Active for the full order-reconciliation window plus an audit retention window. `pending` records that do not reach a terminal state (`matched`, `submission_failed`, `unresolved`, `orphan_ack`) within `sys_config.orders.intent_timeout_minutes` are transitioned to `unresolved` by LADS, not deleted. Terminal records are retained for 12 months in the active tier for dispute resolution and callback-reliability analysis, then move to archive.

**Mechanism**: Online Archive. Archive condition: `created_at` older than 12 months and `status` in a terminal state. Records still in `pending` status older than 12 months are anomalous and must not be archived — they are surfaced to admin for investigation via the normal operational alert path.

**Default archive threshold**: 12 months.

---

### `broker_orders`

**Purpose**: Current and recent FYERS order records synced per user: order ID, symbol, side (BUY/SELL), product type, order type, quantity, filled quantity, status (pending, complete, cancelled, rejected), order timestamp, and last updated timestamp. Used by the chart page to display current active orders and by the reconciliation view to cross-reference platform-initiated orders against broker outcomes. This is a short-lived cache of broker state; the `trade_ledger` remains the accounting source of truth.

**Lifecycle**: Short-lived. Orders older than 30 days from their last-updated timestamp are no longer operationally relevant for display purposes. Records are overwritten on each sync with the latest status from FYERS.

**Mechanism**: TTL index on `updated_at`. Expire documents 30 days after last update.

**Default expiry**: 30 days after last status update.

---

### `equity_curve`

**Purpose**: Append-only time series of per-user portfolio equity recorded at the end of each completed trading session. Each record stores: user ID, session date, total portfolio market value, cumulative realised PnL to date, unrealised PnL, equity high-water mark at that point, and drawdown from high-water mark as a percentage. Used by the RME for drawdown tracking, equity-curve-based sizing adjustments, and historical performance visualisation. Distinct from `portfolio_snapshots`, which is a display cache for the current-state dashboard.

**Lifecycle**: Long-term retention. Records are never modified after writing. Older records move to archive tier.

**Mechanism**: Online Archive. Archive condition: `session_date` older than 36 months.

**Default archive threshold**: 36 months.

---

### `portfolio_snapshots`

**Purpose**: Cached point-in-time portfolio state per user, written after each account sync completion and after each user-triggered manual refresh. Stores: total invested (cost basis), current market value, unrealised PnL, realised PnL, portfolio heat, sector exposure breakdown, cash reserve, equity high-water mark, active position count, and the timestamp and source of the last price data used (SQL Server close or FYERS live quote). The dashboard serves figures from the most recent snapshot on page load, avoiding a live FYERS API call on every visit. The admin portfolio monitoring view reads snapshots across all users to track platform-wide portfolio health. Snapshots are overwritten in place on each update; the previous snapshot is not retained.

**Lifecycle**: Permanent per active user. A snapshot record is maintained for each user indefinitely while their account is active. No archiving is needed since only the latest snapshot per user is kept (the record is updated in place, not appended).

**Mechanism**: No TTL. No archive. One document per user, updated in place.

---

### `manual_adjustments`

**Purpose**: User-initiated manual adjustment entries tied to a specific holding or position context: adjustment type, quantity delta, cost basis delta, reason, and the user and admin who approved it. Audit-critical.

**Lifecycle**: Permanent. Adjustment records must never be deleted. They form part of the reconciliation audit trail.

**Mechanism**: No TTL. No archive.

---

### `fyers_account_sync`

**Purpose**: Sync state metadata per user: last successful sync timestamp for each data type (trades, holdings, orders), sync attempt history, and error state. Used to determine whether incremental sync or backfill is needed.

**Lifecycle**: Active indefinitely (one record per user, updated in place). Sync attempt history entries within the record are pruned after 30 days.

**Mechanism**: TTL is applied to the embedded attempt history array entries via a background clean-up job rather than a document-level TTL index, since only the subdocument history is pruned.

**Default pruning window**: 30 days of attempt history retained.

---

### `rme_profile_recommendations`

**Purpose**: Per-symbol ranked RME profile comparison results: symbol, Signal Subscription, compared profile configurations, outcome metrics per profile, recommended profile ID, and the date the recommendation was generated. Surfaced at position entry and on the strategy configuration screen.

**Lifecycle**: The current recommendation record per symbol is overwritten on each new optimisation run. Superseded recommendations are retained for 12 months.

**Mechanism**: Online Archive. Archive condition: `superseded_at` present and older than 12 months.

**Default archive threshold**: 12 months after supersession.

---

### `universe_upload_results`

**Purpose**: Audit records for each admin CSV upload: upload timestamp, operator identity, file hash, total rows, rows accepted, rows archived, rows excluded, new symbols added, and whether HistoricDataSeed was triggered as a result.

**Lifecycle**: Permanent. Upload results are part of the admin audit trail.

**Mechanism**: No TTL. No archive.

---

### `audit_events`

**Purpose**: Immutable structured audit log for all privileged or sensitive actions: actor identity, action type, target entity, payload summary (no secrets), outcome, and timestamp. Covers admin actions, approval decisions, config changes, kill-switch activation, manual adjustments, and forced re-auth events.

**Lifecycle**: Active for 24 months. Older records move to archive tier for compliance.

**Mechanism**: Online Archive. Archive condition: `event_at` older than 24 months.

**Default archive threshold**: 24 months.

---

### `job_runs`

**Purpose**: Background job execution records: job type, trigger source (scheduled or manual), start and end timestamps, outcome (success, partial, failed), symbols processed, errors encountered, and references to any dependent job success markers written.

**Lifecycle**: Active for 90 days for operational monitoring. Older records move to archive tier.

**Mechanism**: Online Archive. Archive condition: `started_at` older than 90 days.

**Default archive threshold**: 90 days.

---

## Summary Table

| Collection | Mechanism | Active Window / Expiry |
|---|---|---|
| `users` | No expiry | Permanent |
| `sessions` | TTL | Expire 24 hours after issuance |
| `fyers_tokens` | TTL (superseded records) | 30 days after supersession |
| `symbol_master` | No expiry | Permanent |
| `symbol_health` | No expiry (updated in place) | One record per active symbol |
| `watchlists` | No expiry (updated in place) | One record per user, permanent |
| `trading_calendar` | No expiry | Permanent |
| `sys_config` | No expiry | Permanent |
| `signals` | No expiry | Permanent |
| `signal_runs` | Online Archive | Archive after 90 days |
| `entry_signals` | Online Archive | Archive after 12 months |
| `notifications` | Online Archive | Archive after 90 days |
| `notification_bots` | No expiry | Permanent |
| `positions` | Online Archive | Archive 12 months after close |
| `rme_events` | Online Archive | Archive after 24 months |
| `trade_ledger` | Online Archive | Archive after 36 months |
| `intent_ledger` | Online Archive | Archive 12 months after terminal state |
| `broker_orders` | TTL | Expire 30 days after last update |
| `equity_curve` | Online Archive | Archive after 36 months |
| `portfolio_snapshots` | No expiry (updated in place) | One record per user, permanent |
| `manual_adjustments` | No expiry | Permanent |
| `fyers_account_sync` | Background pruning | 30-day attempt history |
| `rme_profile_recommendations` | Online Archive | Archive 12 months after supersession |
| `universe_upload_results` | No expiry | Permanent |
| `audit_events` | Online Archive | Archive after 24 months |
| `job_runs` | Online Archive | Archive after 90 days |

---

## Setup Notes

All default retention thresholds listed above are starting points. Before going live, the admin should review the [Admin Data Management Help](#admin-help-page) section and confirm values are appropriate for the expected data volumes.

Online Archive policies are configured per collection in the MongoDB Atlas console. The archive condition field and threshold period must match the field names and defaults listed above. After archiving, the federated endpoint must be enabled so application queries can reach archived data when needed (for example, FIFO calculations that look back further than 36 months into trade history).

TTL indexes are created as part of the database initialisation scripts. The field name and expiry duration for each TTL-managed collection must match this document.

## Admin Help Page

The admin portal includes a Data Management help page that surfaces:

- the full collection list with its mechanism type, active window, and purpose in plain language
- the default retention thresholds as defined in this document
- a checklist of Online Archive policies to configure in MongoDB Atlas before go-live
- a checklist of TTL indexes to verify after database initialisation
- guidance on enabling the federated query endpoint for archived collections

This page is read-only reference material. It does not control the database configuration directly; it exists to ensure the operator setting up or auditing the database has all defaults and naming conventions in one place.
