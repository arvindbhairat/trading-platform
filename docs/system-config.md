# System Configuration Model

## Purpose

This document defines the shared runtime configuration model for the platform.
Canonical requirements still live in [requirements-spec.md](./requirements-spec.md).

## Configuration Tiers

Use a hybrid configuration model with clear ownership:

### Tier 1: Bootstrap Configuration

Source:

- environment variables
- Azure App Configuration bootstrap settings
- Azure Key Vault or equivalent secret references

Examples:

- MongoDB connection string
- SQL Server connection string
- Redis connection string
- seeded admin email
- Azure App Configuration endpoint
- Key Vault references
- OTLP collector endpoint bootstrap value if needed at startup

Rules:

- required for application startup
- may contain secrets or secret references
- must not depend on MongoDB availability
- must not be editable from the admin portal

### Tier 2: Shared Technical Configuration

Source:

- Azure App Configuration

Examples:

- OTLP collector endpoint
- default log level
- feature-flag defaults
- shared timeout defaults
- environment-specific technical toggles

Rules:

- shared across applications
- centrally managed outside code
- may refresh dynamically where supported
- non-secret values only unless the system uses secret references safely

### Tier 3: Admin-Managed Runtime Configuration

Source:

- MongoDB `sys_config` collection

Examples:

- scan enable or disable toggles
- job retry windows
- z-score default thresholds
- alert throttling values
- UI/runtime operational thresholds

Rules:

- non-secret values only
- editable from the admin portal
- intended for business and operational runtime behavior
- auditable
- not required to bootstrap application startup

## `sys_config` Collection Shape

Each config record should use a stable document shape:

```json
{
  "_id": "ObjectId",
  "key": "jobs.eod_sync.max_retry_count",
  "category": "jobs",
  "valueType": "number",
  "value": 3,
  "defaultValue": 3,
  "description": "Maximum retry attempts for EOD sync job failures.",
  "appliesTo": ["api", "worker"],
  "isEditable": true,
  "requiresRestart": false,
  "validation": {
    "min": 0,
    "max": 10
  },
  "status": "active",
  "updatedAt": "2026-04-04T10:15:00Z",
  "updatedByUserId": "user_123",
  "version": 7
}
```

## Required Fields

- `key`
  - globally unique configuration key
  - dot-delimited naming convention such as `jobs.eod_sync.max_retry_count`
- `category`
  - top-level grouping for admin UX and validation
- `valueType`
  - `string`, `number`, `boolean`, `json`, `string_list`, `number_list`
- `value`
  - current effective value
- `defaultValue`
  - baseline system value used for reset and reference
- `description`
  - human-readable explanation for admin users
- `appliesTo`
  - one or more of `web`, `api`, `worker`, `all`
- `isEditable`
  - whether the admin UI may change it
- `requiresRestart`
  - whether a process restart is required before the value is guaranteed effective
- `validation`
  - rule set for UI and backend validation
- `status`
  - `active`, `deprecated`, or `disabled`
- `updatedAt`
  - last update timestamp
- `updatedByUserId`
  - actor who last changed the value
- `version`
  - monotonically increasing version for optimistic concurrency and audit

## Optional Fields

- `displayName`
  - friendlier UI label than `key`
- `notes`
  - operator notes or rollout guidance
- `tags`
  - free-form classification such as `risk`, `scan`, `observability`
- `effectiveFrom`
  - future activation time if controlled rollout is needed

## Categories

Use these initial categories:

- `jobs`
  - EOD sync retry counts, scan concurrency, retry delays, stale-job cutoffs
- `market_data`
  - quote staleness thresholds, session lag thresholds, data-quality tolerances
- `strategies`
  - default strategy runtime options, parameter bounds, scheduling defaults
- `risk`
  - default max risk per trade, concentration thresholds, suppression thresholds
- `position_sizing`
  - z-score defaults, timeframe defaults, advisory trigger values
- `notifications`
  - Telegram throttling, retry policy, alert grouping options
- `ui`
  - default chart timeframe, list page sizes, refresh intervals
- `feature_flags`
  - runtime feature toggles that are safe for admin control
- `operations`
  - maintenance-mode style toggles, non-secret operational controls
- `integrations`
  - external API rate limits, back-off settings, retry policies, and timeout defaults for third-party providers such as FYERS and Telegram
- `orders`
  - user-initiated order flow thresholds: intent timeout for the reconciliation safety net, callback-reliability alert ratio, signed-payload nonce lifetime
- `rme`
  - Risk Management Engine runtime thresholds that are not position-sizing or stop-loss parameters: per-position channel backlog-depth warning thresholds and other RME operational telemetry limits
- `legal`
  - versioned disclaimer and policy document pointers, user-acknowledgement version identifiers; non-secret values only, audited on change

## What Must Not Go Into `sys_config`

- connection strings
- provider client secrets
- OAuth secrets
- FYERS secret material
- seeded admin email
- Azure credentials
- anything needed before the app can start basic logging and configuration loading

## Key Naming Rules

- use lowercase dot-delimited keys
- group by domain first, then subdomain, then setting
- keep keys stable after release
- do not encode environment names inside keys if each environment has its own database

Examples:

- `jobs.eod_sync.max_retry_count`
- `jobs.eod_sync.retry_delay_seconds`
- `jobs.scan.max_parallel_strategies`
- `market_data.quote.stale_after_seconds`
- `risk.default.max_single_stock_exposure_pct`
- `position_sizing.zscore.default_timeframe`
- `position_sizing.zscore.add_threshold`
- `position_sizing.zscore.reduce_threshold`
- `notifications.telegram.max_retries`
- `ui.chart.default_timeframe`

## Loading Rules

Applications should resolve effective configuration in this order:

1. bootstrap from environment and Azure-managed startup config
2. initialize baseline logging and critical services
3. load shared technical config
4. load relevant `sys_config` runtime values
5. validate and cache effective runtime config for the process

If `sys_config` is temporarily unavailable:

- startup-critical paths must still work from bootstrap config
- runtime features depending on `sys_config` should degrade safely
- the platform should log the failure and use safe defaults where possible

### Empty-database Boot Sequence

*Canonical requirements: REQ-CONFIG-010 and REQ-CONFIG-011. See also REQ-ROLE-005.*

On a first deploy the database is empty: no `sys_config` rows exist and no admin user record exists. The platform resolves this deterministically through an explicit pipeline-ordered sequence, not through side-effects of Worker or API startup:

1. **Azure DevOps release pipeline — migrations and seeding step:** runs in this order and all must succeed before API/Worker activation:
   1. FluentMigrator against the SQL Server market-data and backtest databases.
   2. Mongo.Migration against MongoDB for schema-shape evolution.
   3. The `sys_config` seeder console application (REQ-CONFIG-011): reads the manifest mirroring the Required Seed Table below, inserts missing rows only (never overwrites existing rows), resolves per-deployment keys from pipeline parameters, writes the `platform.seed.version` sentinel with the release identifier, and emits a structured report artefact.
2. **Worker/API startup sentinel check:** both services read the `platform.seed.version` row on startup and fail fast with a clear error if it is absent. This turns a skipped seeder step into a loud, actionable failure instead of silent self-repair or a circular dependency on an unseeded `sys_config`.
3. **Bootstrap admin creation (REQ-ROLE-005):** the admin user record is *not* pre-seeded. It is created lazily on the admin's first successful OAuth sign-in when the authenticated email matches the bootstrap `SEED_ADMIN_EMAIL`. Before that first sign-in, no approval flows can run (REQ-ROLE-004 blocks all protected features, so no non-admin users can generate approval requests), which is why no placeholder admin row is needed and why `sys_config` seeding is not gated on admin existence.
4. **Upgrades:** every subsequent deployment re-runs the same pipeline. New manifest keys are inserted by the seeder; existing keys are left untouched so in-portal admin edits (REQ-CONFIG-005) survive redeploys. The sentinel row is upserted with the new release identifier on every run.

Per-deployment keys — those flagged `*(seeded per deployment — see REQ-...)*` in the seed table — must be resolved from pipeline parameters for the target environment; the seeder fails the pipeline with the list of unresolved keys if any is missing, preventing a partial-seed deployment.

## Admin Management Rules

The admin page should support:

- list and search config keys
- filter by category and target application
- edit only `isEditable = true` records
- view description, validation rules, current value, and default value
- reset a key to default value
- see update history and last modified actor
- block edits to non-editable or secret/bootstrap settings

## Required Seed Table

This table is the authoritative reference for the database seeding script (REQ-CONFIG-009). Every `sys_config` key that any requirement mandates must appear here. When adding a new requirement that introduces a key, add a row to this table at the same time.

| Key | Category | Type | Default | Requirement |
|---|---|---|---|---|
| `jobs.eod_sync.max_retry_count` | jobs | number | 3 | REQ-MARKET-005 |
| `jobs.eod_sync.retry_delay_seconds` | jobs | number | 60 | REQ-MARKET-005 |
| `jobs.scan.max_parallel_strategies` | jobs | number | 5 | REQ-STRAT-013 |
| `jobs.scan.abort_on_missing_success_marker` | jobs | boolean | true | REQ-MARKET-007 |
| `jobs.live_market_scan.poll_interval_seconds` | jobs | number | 90 | REQ-STOP-006 |
| `jobs.lmds.capacity_warn_pct` | jobs | number | 80 | REQ-SLO-011 |
| `jobs.admin_token_check.daily_check_time` | jobs | string | `15:00` | REQ-NOTIFY-022 |
| `jobs.ledger_lock.ttl_seconds` | jobs | number | 600 | REQ-PORT-031 |
| `jobs.ledger_lock.renewal_interval_seconds` | jobs | number | 60 | REQ-PORT-031 |
| `jobs.ledger_lock.skip_warn_threshold` | jobs | number | 3 | REQ-PORT-031 |
| `market_data.halt.consecutive_stale_polls` | market_data | number | 3 | REQ-HALT-001 |
| `market_data.halt.stale_symbol_pct` | market_data | number | 80 | REQ-HALT-001 |
| `jobs.account_sync.intraday_interval_minutes` | jobs | number | 15 | REQ-PORT-019 |
| `jobs.account_sync.manual_sync_min_interval_seconds` | jobs | number | 120 | REQ-PORT-021 |
| `market_data.quote.stale_after_seconds` | market_data | number | 300 | REQ-DASH-013 |
| `market_data.eod.skip_symbol_if_daily_bar_missing` | market_data | boolean | true | REQ-MARKET-006 |
| `market_data.validation.max_invalid_candle_pct` | market_data | number | 5 | REQ-MARKET-015 |
| `risk.default.max_single_stock_exposure_pct` | risk | number | 10 | REQ-SIZING-012 |
| `risk.default.max_total_open_risk_pct` | risk | number | 5 | REQ-SIZING-013 |
| `risk.default.max_sector_exposure_pct` | risk | number | 20 | REQ-SIZING-014 |
| `risk.default.max_concurrent_positions` | risk | number | 20 | REQ-HEAT-005 |
| `risk.default.min_cash_reserve_pct` | risk | number | 20 | REQ-HEAT-007 |
| `risk.default.max_portfolio_heat_pct` | risk | number | 5 | REQ-HEAT-002 |
| `risk.drawdown.daily_loss_limit_pct` | risk | number | 2 | REQ-DRDN-002 |
| `risk.drawdown.weekly_loss_limit_pct` | risk | number | 4 | REQ-DRDN-002 |
| `risk.drawdown.monthly_loss_limit_pct` | risk | number | 6 | REQ-DRDN-002 |
| `risk.drawdown.threshold_reduce_size_pct` | risk | number | 5 | REQ-DRDN-003 |
| `risk.drawdown.threshold_suspend_addon_pct` | risk | number | 10 | REQ-DRDN-003 |
| `risk.drawdown.threshold_reduce_positions_pct` | risk | number | 15 | REQ-DRDN-003 |
| `risk.drawdown.threshold_block_new_entries_pct` | risk | number | 20 | REQ-DRDN-003 |
| `risk.drawdown.threshold_close_weakest_pct` | risk | number | 25 | REQ-DRDN-003 |
| `risk.drawdown.threshold_stop_all_trading_pct` | risk | number | 30 | REQ-DRDN-003 |
| `risk.pending_entry.expiry_sessions` | risk | number | 1 | REQ-PLC-008 |
| `risk.time_stop.default_sessions` | risk | number | 10 | REQ-STOP-003 |
| `risk.pyramiding.max_addon_entries` | risk | number | 3 | REQ-PYR-003 |
| `risk.stop.breakeven_trigger_r` | risk | number | 1 | REQ-STOP-009 |
| `risk.equity_curve_sizing.enabled` | risk | boolean | false | REQ-DRDN-006 |
| `risk.equity_curve_sizing.lookback_sessions` | risk | number | 20 | REQ-DRDN-006 |
| `risk.concentration.addon_block_heat_threshold_pct` | risk | number | 4 | REQ-HEAT-006 |
| `risk.concentration.addon_block_sector_exposure_pct` | risk | number | 15 | REQ-HEAT-006 |
| `risk.corporate_action.avg_cost_delta_threshold_pct` | risk | number | 50 | REQ-PORT-016 |
| `risk.circuit_limit_stale_minutes` | risk | number | 15 | REQ-ORDER-018a |
| `risk.equity_override_stale_days` | risk | number | 30 | REQ-RME-006c |
| `rme.channel.backlog_warn_depth` | rme | number | 20 | REQ-RME-CONC-004 |
| `position_sizing.default_risk_per_trade_pct` | position_sizing | number | 0.5 | REQ-SIZING-005 |
| `position_sizing.max_risk_per_trade_pct` | position_sizing | number | 1.0 | REQ-SIZING-005 |
| `position_sizing.zscore.default_timeframe` | position_sizing | string | `rolling_5d` | REQ-SIZING-007 |
| `position_sizing.zscore.add_threshold` | position_sizing | number | 3 | REQ-SIZING-007 |
| `position_sizing.zscore.reduce_threshold` | position_sizing | number | -3 | REQ-SIZING-007 |
| `notifications.telegram.max_retries` | notifications | number | 5 | REQ-NOTIFY-008 |
| `notifications.telegram.retry_backoff_seconds` | notifications | number | 30 | REQ-NOTIFY-008 |
| `notifications.telegram.linking_token_expiry_minutes` | notifications | number | 15 | REQ-NOTIFY-016 |
| `notifications.telegram.dirty_link_failure_threshold` | notifications | number | 10 | REQ-NOTIFY-019 |
| `notifications.telegram.rotation_days` | notifications | number | 90 | REQ-NOTIFY-022a (bot token rotation cadence; admin prompted to rotate when token age exceeds this threshold) |
| `notifications.telegram.global_disable` | notifications | boolean | false | REQ-NOTIFY-022a (break-glass flag — when true the Notification Delivery Job skips all Telegram dispatch for all users; requires step-up re-auth to change) |
| `ui.chart.default_timeframe` | ui | string | `daily` | REQ-TIMEFRAME-001 |
| `ui.session.expiry_warning_minutes` | ui | number | 30 | REQ-SESSION-007 |
| `integrations.fyers.rate_limit.per_second` | integrations | number | 10 | REQ-RATE-003 |
| `integrations.fyers.rate_limit.per_minute` | integrations | number | 200 | REQ-RATE-003 |
| `integrations.fyers.rate_limit.per_day` | integrations | number | 100000 | REQ-RATE-003 |
| `integrations.fyers.rate_limit.backoff_initial_ms` | integrations | number | 1000 | REQ-RATE-004 |
| `integrations.fyers.rate_limit.backoff_max_ms` | integrations | number | 60000 | REQ-RATE-004 |
| `integrations.fyers.rate_limit.backoff_multiplier` | integrations | number | 2 | REQ-RATE-004 |
| `integrations.fyers.widget_callback_timeout_seconds` | integrations | number | 10 | REQ-ORDER-017a |
| `integrations.fyers.quote_delay_seconds` | integrations | number | 0 | REQ-STOP-006c |
| `orders.intent_timeout_minutes` | orders | number | 10 | REQ-ORDER-015c |
| `orders.callback_failure_alert_ratio` | orders | number | 0.10 | REQ-ORDER-015d |
| `orders.payload_nonce_ttl_seconds` | orders | number | 300 | REQ-ORDER-009b |
| `orders.cnc_probe_schedule` | orders | string | `never` | REQ-ORDER-010c (CNC sandbox probe re-run schedule; `never` means manual-only re-probe; a cron expression such as `0 9 * * 1` schedules automatic re-probing) |
| `orders.execution_assistance_mode` | orders | string | `widget` | REQ-ORDER-010c (current execution-assistance mode — `widget` or `advisory_only`; written only by probe execution, not directly editable via admin UI) |
| `orders.execution_assistance_mode_updated_at` | orders | string | *(seeded per deployment)* | REQ-ORDER-010c (UTC timestamp of the most recent execution-assistance mode transition; written by probe execution path) |
| `integrations.truedata.rate_limit.per_second` | integrations | number | 10 | REQ-RATE-009 |
| `integrations.truedata.rate_limit.per_minute` | integrations | number | 200 | REQ-RATE-009 |
| `integrations.truedata.rate_limit.per_day` | integrations | number | 100000 | REQ-RATE-009 |
| `integrations.truedata.rate_limit.backoff_initial_ms` | integrations | number | 1000 | REQ-RATE-009 |
| `integrations.truedata.rate_limit.backoff_max_ms` | integrations | number | 60000 | REQ-RATE-009 |
| `integrations.truedata.quote_delay_seconds` | integrations | number | 900 | REQ-STOP-006c |
| `integrations.gdf.rate_limit.per_second` | integrations | number | 10 | REQ-RATE-009 |
| `integrations.gdf.rate_limit.per_minute` | integrations | number | 200 | REQ-RATE-009 |
| `integrations.gdf.rate_limit.per_day` | integrations | number | 100000 | REQ-RATE-009 |
| `integrations.gdf.rate_limit.backoff_initial_ms` | integrations | number | 1000 | REQ-RATE-009 |
| `integrations.gdf.rate_limit.backoff_max_ms` | integrations | number | 60000 | REQ-RATE-009 |
| `integrations.gdf.quote_delay_seconds` | integrations | number | 900 | REQ-STOP-006c |
| `strategies.backtest.low_confidence_skip_threshold_pct` | strategies | number | 5 | REQ-STRAT-023 |
| `strategies.backtest.survivorship_bias_discount_pct` | strategies | number | 15 | REQ-STRAT-011b |
| `strategies.backtest.draft_expiry_days` | strategies | number | 7 | REQ-BTSTORE-009a |
| `market_data.provider.active` | market_data | string | `fyers` | REQ-MKTPROV-005 |
| `integrations.fyers.price_deviation_warning_pct` | integrations | number | 1 | REQ-ORDER-011 |
| `risk.default.portfolio_heat_warning_threshold_pct` | risk | number | 4 | REQ-ORDER-012 |
| `operations.system_health.lookback_days` | operations | number | 7 | REQ-ADMIN-014 |
| `operations.phase.current` | operations | string | `A` | REQ-LEGAL-001 |
| `operations.phase_a.tester_ceiling` | operations | number | 30 | REQ-LEGAL-003 |
| `legal.disclaimer.short_text` | legal | string | *(seeded per deployment — see REQ-LEGAL-007)* | REQ-LEGAL-007 |
| `legal.disclaimer.long_url` | legal | string | *(seeded per deployment — see REQ-LEGAL-007)* | REQ-LEGAL-007 |
| `legal.disclaimer.long_version` | legal | string | `v1` | REQ-LEGAL-007 |
| `legal.tos.current_version` | legal | string | `v1` | REQ-LEGAL-008 |
| `legal.privacy.current_version` | legal | string | `v1` | REQ-LEGAL-008 |
| `legal.tester_acknowledgement.current_version` | legal | string | `v1` | REQ-LEGAL-004 |
| `operations.universe.stale_after_days` | operations | number | 180 | REQ-UNIV-016 |
| `operations.universe.archive_confirm_threshold_pct` | operations | number | 5 | REQ-UNIV-018 |
| `operations.universe.rollback_retention_days` | operations | number | 30 | REQ-UNIV-019 |
| `operations.universe.symbol_probe_enabled` | operations | boolean | true | REQ-UNIV-021 |
| `operations.universe.symbol_probe_batch_size` | operations | number | 50 | REQ-UNIV-021 |
| `operations.universe.symbol_probe_flag_threshold_days` | operations | number | 2 | REQ-UNIV-021b |
| `operations.impersonation.idle_timeout_minutes` | operations | number | 15 | REQ-ADMIN-015 |
| `operations.worker.singleton_lease_ttl_seconds` | operations | number | 60 | REQ-RME-CONC-006 |
| `rme.channel.backlog_warn_depth` | rme | number | 20 | REQ-RME-CONC-004 |
| `rme.equity_read.snapshot_retry_max_attempts` | rme | number | 3 | REQ-RME-006d |
| `rme.equity_read.snapshot_retry_backoff_ms` | rme | number | 50 | REQ-RME-006d |
| `notifications.email.sender_domain` | notifications | string | *(seeded per deployment — see REQ-NOTIFY-020)* | REQ-NOTIFY-020 |
| `notifications.email.sender_address` | notifications | string | *(seeded per deployment — see REQ-NOTIFY-020)* | REQ-NOTIFY-020 |
| `config.last_known_good.max_age_seconds` | operations | number | 86400 | ADR-0002 (App Config startup-failure policy) — bootstrap-tier only; resolved from LKG cache or env vars, not MongoDB |
| `session.fyers.lease_ttl_seconds` | operations | number | 1800 | REQ-SESSION-014 |
| `integrations.fyers.rate_limit.reset_boundary` | integrations | string | `utc_midnight` | REQ-RATE-012 (budget projection reset boundary; override per provider) |
| `dashboard.portfolio_snapshot.ttl_seconds` | dashboard | number | 900 | REQ-DASH-018 |
| `eodsr.db_retry.max_seconds` | operations | number | 120 | REQ-STRAT-027 |
| `orders.widget_timeout_seconds` | operations | number | 120 | REQ-ORDER-018b |
| `platform.seed.version` | operations | string | *(seeded per deployment — release identifier written by the sys_config seeder, see REQ-CONFIG-011)* | REQ-CONFIG-010 |

## Implementation Guidance

- keep `sys_config` reads behind a dedicated configuration service
- cache values in-process with clear refresh strategy
- validate config both at write time and read time
- include config version and key in important logs
- audit every config change
