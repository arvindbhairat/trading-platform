namespace SignalStack.Seed;

/// <summary>
/// Canonical manifest mirroring the Required Seed Table in docs/system-config.md.
/// Frozen-after-author for the seeder topology; manifest evolves with REQ additions.
/// </summary>
public static class SysConfigManifest
{
    public static IReadOnlyList<ConfigSeedEntry> All { get; } = Build();

    private static List<ConfigSeedEntry> Build() =>
    [
        // ── jobs ──────────────────────────────────────────────────────────
        new("jobs.eod_sync.max_retry_count",           "jobs",           "number",  "3",   "Maximum retry attempts for EOD sync job failures.",                                                                   "REQ-MARKET-005"),
        new("jobs.eod_sync.retry_delay_seconds",       "jobs",           "number",  "60",  "Delay between EOD sync retry attempts.",                                                                              "REQ-MARKET-005"),
        new("jobs.scan.max_parallel_strategies",       "jobs",           "number",  "5",   "Maximum number of strategies evaluated in parallel per scan run.",                                                       "REQ-STRAT-013"),
        new("jobs.scan.abort_on_missing_success_marker","jobs",          "boolean", "true","Abort scan if the EOD success marker is missing.",                                                                    "REQ-MARKET-007"),
        new("jobs.live_market_scan.poll_interval_seconds","jobs",         "number",  "90",  "Poll interval for the Live Market Data Scan.",                                                                       "REQ-STOP-006"),
        new("jobs.lmds.capacity_warn_pct",             "jobs",           "number",  "80",  "LMDS capacity warning threshold as percentage of max symbols.",                                                     "REQ-SLO-011"),
        new("jobs.admin_token_check.daily_check_time", "jobs",           "string",  "15:00","Daily admin token check time in IST.",                                                                                 "REQ-NOTIFY-022"),
        new("jobs.admin_token_check.pre_market_check_time","jobs",        "string",  "08:30","Pre-market admin token check time in IST.",                                                                           "REQ-NOTIFY-022b"),
        new("jobs.ledger_lock.ttl_seconds",            "jobs",           "number",  "600",  "TTL for trade-ledger write lock.",                                                                                    "REQ-PORT-031"),
        new("jobs.ledger_lock.renewal_interval_seconds","jobs",          "number",  "60",   "Renewal interval for trade-ledger write lock.",                                                                      "REQ-PORT-031"),
        new("jobs.ledger_lock.skip_warn_threshold",    "jobs",           "number",  "3",    "Consecutive lock-skip attempts before warning.",                                                                     "REQ-PORT-031"),
        new("jobs.account_sync.intraday_interval_minutes","jobs",        "number",  "15",   "Intraday interval for LADS account sync.",                                                                           "REQ-PORT-019"),
        new("jobs.account_sync.manual_sync_min_interval_seconds","jobs",  "number",  "120",  "Minimum interval between manual syncs.",                                                                              "REQ-PORT-021"),

        // ── market_data ───────────────────────────────────────────────────
        new("market_data.halt.consecutive_stale_polls","market_data",    "number",  "3",    "Consecutive stale polls triggering halt detection.",                                                                "REQ-HALT-001"),
        new("market_data.halt.stale_symbol_pct",       "market_data",    "number",  "80",   "Percentage of stale symbols required for halt declaration.",                                                        "REQ-HALT-001"),
        new("market_data.quote.stale_after_seconds",   "market_data",    "number",  "300",  "Seconds after which a quote is considered stale.",                                                                  "REQ-DASH-013"),
        new("market_data.eod.skip_symbol_if_daily_bar_missing","market_data","boolean","true","Skip symbols in EOD sync when daily bar is missing.",                                                               "REQ-MARKET-006"),
        new("market_data.validation.max_invalid_candle_pct","market_data","number",  "5",    "Maximum percentage of invalid candles before rejection.",                                                           "REQ-MARKET-015"),
        new("market_data.provider.active",             "market_data",    "string",  "fyers", "Active market data provider.",                                                                                        "REQ-MKTPROV-005"),

        // ── risk ──────────────────────────────────────────────────────────
        new("risk.default.max_single_stock_exposure_pct","risk",         "number",  "10",   "Maximum single-stock exposure as percentage of equity.",                                                            "REQ-SIZING-012"),
        new("risk.default.max_total_open_risk_pct",    "risk",           "number",  "5",    "Maximum total open risk as percentage of equity.",                                                                  "REQ-SIZING-013"),
        new("risk.default.max_sector_exposure_pct",    "risk",           "number",  "20",   "Maximum sector exposure as percentage of equity.",                                                                  "REQ-SIZING-014"),
        new("risk.default.max_concurrent_positions",   "risk",           "number",  "20",   "Maximum concurrent positions.",                                                                                      "REQ-HEAT-005"),
        new("risk.default.min_cash_reserve_pct",       "risk",           "number",  "20",   "Minimum cash reserve as percentage of equity.",                                                                     "REQ-HEAT-007"),
        new("risk.default.max_portfolio_heat_pct",     "risk",           "number",  "5",    "Maximum portfolio heat as percentage of equity.",                                                                   "REQ-HEAT-002"),
        new("risk.drawdown.daily_loss_limit_pct",      "risk",           "number",  "2",    "Daily loss limit before drawdown enforcement.",                                                                     "REQ-DRDN-002"),
        new("risk.drawdown.weekly_loss_limit_pct",     "risk",           "number",  "4",    "Weekly loss limit before drawdown enforcement.",                                                                    "REQ-DRDN-002"),
        new("risk.drawdown.monthly_loss_limit_pct",    "risk",           "number",  "6",    "Monthly loss limit before drawdown enforcement.",                                                                   "REQ-DRDN-002"),
        new("risk.drawdown.threshold_reduce_size_pct", "risk",           "number",  "5",    "Drawdown threshold: reduce position sizes.",                                                                        "REQ-DRDN-003"),
        new("risk.drawdown.threshold_suspend_addon_pct","risk",          "number",  "10",   "Drawdown threshold: suspend add-on entries.",                                                                       "REQ-DRDN-003"),
        new("risk.drawdown.threshold_reduce_positions_pct","risk",       "number",  "15",   "Drawdown threshold: reduce position count.",                                                                        "REQ-DRDN-003"),
        new("risk.drawdown.threshold_block_new_entries_pct","risk",      "number",  "20",   "Drawdown threshold: block new entries.",                                                                            "REQ-DRDN-003"),
        new("risk.drawdown.threshold_close_weakest_pct","risk",          "number",  "25",   "Drawdown threshold: close weakest positions.",                                                                      "REQ-DRDN-003"),
        new("risk.drawdown.threshold_stop_all_trading_pct","risk",       "number",  "30",   "Drawdown threshold: stop all trading.",                                                                             "REQ-DRDN-003"),
        new("risk.pending_entry.expiry_sessions",      "risk",           "number",  "1",    "Number of sessions before a pending entry expires.",                                                                "REQ-PLC-008"),
        new("risk.time_stop.default_sessions",         "risk",           "number",  "10",   "Default session count for time-stop evaluation.",                                                                   "REQ-STOP-003"),
        new("risk.pyramiding.max_addon_entries",       "risk",           "number",  "3",    "Maximum add-on entries per position.",                                                                              "REQ-PYR-003"),
        new("risk.stop.breakeven_trigger_r",           "risk",           "number",  "1",    "R-multiple threshold for breakeven stop activation.",                                                               "REQ-STOP-009"),
        new("risk.equity_curve_sizing.enabled",        "risk",           "boolean","false","Enable equity-curve-based position sizing.",                                                                         "REQ-DRDN-006"),
        new("risk.equity_curve_sizing.lookback_sessions","risk",         "number",  "20",   "Lookback sessions for equity-curve sizing.",                                                                        "REQ-DRDN-006"),
        new("risk.concentration.addon_block_heat_threshold_pct","risk",  "number",  "4",    "Portfolio heat percentage at which add-ons are blocked.",                                                           "REQ-HEAT-006"),
        new("risk.concentration.addon_block_sector_exposure_pct","risk", "number",  "15",   "Sector exposure percentage at which add-ons are blocked.",                                                          "REQ-HEAT-006"),
        new("risk.corporate_action.avg_cost_delta_threshold_pct","risk", "number",  "50",   "Average-cost delta percentage triggering corporate-action detection.",                                               "REQ-PORT-016"),
        new("risk.circuit_limit_stale_minutes",        "risk",           "number",  "15",   "Minutes after which a circuit-limit reading is considered stale.",                                                  "REQ-ORDER-018a"),
        new("risk.equity_override_stale_days",         "risk",           "number",  "30",   "Days after which an equity-base override is considered stale.",                                                     "REQ-RME-006c"),
        new("risk.default.portfolio_heat_warning_threshold_pct","risk",  "number",  "4",    "Portfolio heat warning threshold percentage.",                                                                      "REQ-ORDER-012"),

        // ── rme ───────────────────────────────────────────────────────────
        new("rme.channel.backlog_warn_depth",          "rme",            "number",  "20",   "Per-position channel backlog depth warning threshold.",                                                             "REQ-RME-CONC-004"),
        new("rme.equity_read.snapshot_retry_max_attempts","rme",         "number",  "3",    "Maximum retry attempts for equity snapshot read.",                                                                  "REQ-RME-006d"),
        new("rme.equity_read.snapshot_retry_backoff_ms","rme",           "number",  "50",   "Backoff in milliseconds between equity snapshot retries.",                                                          "REQ-RME-006d"),

        // ── position_sizing ───────────────────────────────────────────────
        new("position_sizing.default_risk_per_trade_pct","position_sizing","number","0.5",  "Default risk per trade as percentage of equity.",                                                                   "REQ-SIZING-005"),
        new("position_sizing.max_risk_per_trade_pct",  "position_sizing","number",  "1.0",  "Maximum risk per trade as percentage of equity.",                                                                   "REQ-SIZING-005"),
        new("position_sizing.zscore.default_timeframe","position_sizing","string",  "rolling_5d","Default look-back timeframe for z-score calculation.",                                                          "REQ-SIZING-007"),
        new("position_sizing.zscore.add_threshold",    "position_sizing","number",  "3",    "Z-score threshold triggering add-on entry.",                                                                        "REQ-SIZING-007"),
        new("position_sizing.zscore.reduce_threshold", "position_sizing","number",  "-3",   "Z-score threshold triggering position reduction.",                                                                  "REQ-SIZING-007"),

        // ── notifications ─────────────────────────────────────────────────
        new("notifications.telegram.max_retries",      "notifications",  "number",  "5",    "Maximum retry attempts for Telegram dispatch.",                                                                     "REQ-NOTIFY-008"),
        new("notifications.telegram.retry_backoff_seconds","notifications","number","30",   "Backoff in seconds between Telegram retries.",                                                                      "REQ-NOTIFY-008"),
        new("notifications.telegram.linking_token_expiry_minutes","notifications","number","15","Telegram linking token expiry in minutes.",                                                                       "REQ-NOTIFY-016"),
        new("notifications.telegram.dirty_link_failure_threshold","notifications","number","10","Consecutive Telegram failures before auto-disable.",                                                             "REQ-NOTIFY-019"),
        new("notifications.telegram.rotation_days",    "notifications",  "number",  "90",   "Bot token rotation cadence in days.",                                                                               "REQ-NOTIFY-022a"),
        new("notifications.telegram.global_disable",   "notifications",  "boolean","false","Break-glass flag to disable all Telegram dispatch.",                                                                "REQ-NOTIFY-022a"),

        // ── ui ────────────────────────────────────────────────────────────
        new("ui.chart.default_timeframe",              "ui",             "string",  "daily","Default chart timeframe.",                                                                                            "REQ-TIMEFRAME-001"),
        new("ui.session.expiry_warning_minutes",       "ui",             "number",  "30",   "Minutes before session expiry to show warning.",                                                                    "REQ-SESSION-007"),

        // ── integrations ──────────────────────────────────────────────────
        new("integrations.fyers.rate_limit.per_second","integrations",   "number",  "10",    "FYERS API rate limit: requests per second.",                                                                       "REQ-RATE-003"),
        new("integrations.fyers.rate_limit.per_minute","integrations",   "number",  "200",   "FYERS API rate limit: requests per minute.",                                                                       "REQ-RATE-003"),
        new("integrations.fyers.rate_limit.per_day",   "integrations",   "number",  "100000","FYERS API rate limit: requests per day.",                                                                          "REQ-RATE-003"),
        new("integrations.fyers.rate_limit.backoff_initial_ms","integrations","number","1000","FYERS API rate-limit backoff: initial delay in ms.",                                                               "REQ-RATE-004"),
        new("integrations.fyers.rate_limit.backoff_max_ms","integrations","number", "60000", "FYERS API rate-limit backoff: maximum delay in ms.",                                                              "REQ-RATE-004"),
        new("integrations.fyers.rate_limit.backoff_multiplier","integrations","number","2",  "FYERS API rate-limit backoff: multiplier per retry.",                                                              "REQ-RATE-004"),
        new("integrations.fyers.widget_callback_timeout_seconds","integrations","number","10","FYERS widget callback timeout in seconds.",                                                                        "REQ-ORDER-017a"),
        new("integrations.fyers.quote_delay_seconds",  "integrations",   "number",  "0",    "FYERS quote delay in seconds (0 = real-time).",                                                                     "REQ-STOP-006c"),
        new("integrations.fyers.price_deviation_warning_pct","integrations","number","1",   "Price deviation warning threshold percentage.",                                                                     "REQ-ORDER-011"),
        new("integrations.fyers.rate_limit.reset_boundary","integrations","string", "utc_midnight","Budget projection reset boundary for FYERS rate limits.",                                                    "REQ-RATE-012"),

        new("integrations.truedata.rate_limit.per_second","integrations","number", "10",    "TrueData API rate limit: requests per second.",                                                                     "REQ-RATE-009"),
        new("integrations.truedata.rate_limit.per_minute","integrations","number", "200",   "TrueData API rate limit: requests per minute.",                                                                     "REQ-RATE-009"),
        new("integrations.truedata.rate_limit.per_day", "integrations",  "number",  "100000","TrueData API rate limit: requests per day.",                                                                        "REQ-RATE-009"),
        new("integrations.truedata.rate_limit.backoff_initial_ms","integrations","number","1000","TrueData API rate-limit backoff: initial delay in ms.",                                                           "REQ-RATE-009"),
        new("integrations.truedata.rate_limit.backoff_max_ms","integrations","number","60000","TrueData API rate-limit backoff: maximum delay in ms.",                                                          "REQ-RATE-009"),
        new("integrations.truedata.quote_delay_seconds","integrations",  "number",  "900",  "TrueData quote delay in seconds.",                                                                                   "REQ-STOP-006c"),

        new("integrations.gdf.rate_limit.per_second",  "integrations",   "number",  "10",    "GDF API rate limit: requests per second.",                                                                          "REQ-RATE-009"),
        new("integrations.gdf.rate_limit.per_minute",  "integrations",   "number",  "200",   "GDF API rate limit: requests per minute.",                                                                          "REQ-RATE-009"),
        new("integrations.gdf.rate_limit.per_day",     "integrations",   "number",  "100000","GDF API rate limit: requests per day.",                                                                             "REQ-RATE-009"),
        new("integrations.gdf.rate_limit.backoff_initial_ms","integrations","number","1000","GDF API rate-limit backoff: initial delay in ms.",                                                               "REQ-RATE-009"),
        new("integrations.gdf.rate_limit.backoff_max_ms","integrations","number", "60000",  "GDF API rate-limit backoff: maximum delay in ms.",                                                              "REQ-RATE-009"),
        new("integrations.gdf.quote_delay_seconds",    "integrations",   "number",  "900",  "GDF quote delay in seconds.",                                                                                       "REQ-STOP-006c"),

        // ── strategies ────────────────────────────────────────────────────
        new("strategies.backtest.low_confidence_skip_threshold_pct","strategies","number","5","Backtest low-confidence skip threshold percentage.",                                                               "REQ-STRAT-023"),
        new("strategies.backtest.survivorship_bias_discount_pct","strategies","number","15","Survivorship-bias discount percentage in backtest results.",                                                       "REQ-STRAT-011b"),
        new("strategies.backtest.draft_expiry_days",   "strategies",     "number",  "7",    "Days after which a draft backtest expires.",                                                                        "REQ-BTSTORE-009a"),

        // ── operations ────────────────────────────────────────────────────
        new("operations.phase.current",                "operations",     "string",  "A",    "Current operating phase.",                                                                                           "REQ-LEGAL-001"),
        new("operations.phase_a.tester_ceiling",       "operations",     "number",  "30",   "Maximum number of approved tester accounts in Phase A.",                                                            "REQ-LEGAL-003"),
        new("operations.system_health.lookback_days",  "operations",     "number",  "7",    "Look-back window for system health metrics.",                                                                       "REQ-ADMIN-014"),
        new("operations.universe.stale_after_days",    "operations",     "number",  "180",  "Days after which a symbol is considered stale.",                                                                    "REQ-UNIV-016"),
        new("operations.universe.archive_confirm_threshold_pct","operations","number","5",  "Archive confirmation threshold percentage.",                                                                         "REQ-UNIV-018"),
        new("operations.universe.rollback_retention_days","operations",  "number",  "30",   "Rollback retention period in days.",                                                                                "REQ-UNIV-019"),
        new("operations.universe.symbol_probe_enabled","operations",     "boolean","true", "Enable automatic symbol validity probing.",                                                                          "REQ-UNIV-021"),
        new("operations.universe.symbol_probe_batch_size","operations",  "number",  "50",   "Batch size for symbol validity probe.",                                                                             "REQ-UNIV-021"),
        new("operations.universe.symbol_probe_flag_threshold_days","operations","number","2","Days of consecutive failure before flagging a symbol.",                                                            "REQ-UNIV-021b"),
        new("operations.universe.gap_repair_threshold_sessions","operations","number","10","Sessions gap before a symbol is flagged for explicit repair.",                                                    "REQ-MARKET-017"),
        new("operations.impersonation.idle_timeout_minutes","operations","number",  "15",   "Idle timeout for admin impersonation sessions.",                                                                     "REQ-ADMIN-015"),
        new("operations.worker.singleton_lease_ttl_seconds","operations","number",  "60",   "TTL for the Worker singleton lease.",                                                                               "REQ-RME-CONC-006"),
        new("config.last_known_good.max_age_seconds",  "operations",     "number",  "86400","Maximum age of LKG cache before fallback is considered stale.",                                                    "ADR-0002"),
        new("config.last_known_good.staleness_alert_threshold_seconds","operations","number","21600","Staleness threshold for LKG cache alert.",                                                                     "ADR-0002"),
        new("session.fyers.lease_ttl_seconds",         "operations",     "number",  "1800", "TTL for the FYERS WebSocket PLD lease.",                                                                            "REQ-SESSION-014"),
        new("eodsr.db_retry.max_seconds",              "operations",     "number",  "120",  "Maximum retry duration for EODSR database operations.",                                                            "REQ-STRAT-027"),
        new("operations.bte_rme_parity.enabled",       "operations",     "boolean","true", "Controls whether the nightly BTE-vs-RME parity check pipeline runs.",                                               "REQ-NFR-017"),

        // ── orders ────────────────────────────────────────────────────────
        new("orders.intent_timeout_minutes",           "orders",         "number",  "10",   "Timeout in minutes before an unresolved intent is considered orphaned.",                                           "REQ-ORDER-015c"),
        new("orders.callback_failure_alert_ratio",     "orders",         "number",  "0.10", "Callback failure ratio above which an admin alert fires.",                                                          "REQ-ORDER-015d"),
        new("orders.payload_nonce_ttl_seconds",        "orders",         "number",  "300",  "TTL for signed-payload nonce in seconds.",                                                                          "REQ-ORDER-009b"),
        new("orders.cnc_probe_schedule",               "orders",         "string",  "never","CNC sandbox probe re-run schedule; 'never' means manual-only.",                                                   "REQ-ORDER-010c"),
        new("orders.intent.hmac_key_name",             "orders",         "string",  "orders-intent-hmac-key","Key Vault secret name for the intent payload HMAC signing key.",                                        "REQ-ORDER-009b"),
        new("orders.execution_assistance_mode",        "orders",         "string",  "widget","Current execution-assistance mode: 'widget' or 'advisory_only'.",                                                "REQ-ORDER-010c"),
        new("orders.widget_timeout_seconds",           "operations",     "number",  "120",  "Timeout for the FYERS execution widget.",                                                                           "REQ-ORDER-018b"),

        // ── dashboard ─────────────────────────────────────────────────────
        new("dashboard.portfolio_snapshot.ttl_seconds", "dashboard",     "number",  "900",  "TTL for cached portfolio snapshot.",                                                                                "REQ-DASH-018"),

        // ── legal ─────────────────────────────────────────────────────────
        new("legal.tos.current_version",               "legal",          "string",  "v1",   "Current Terms of Service version identifier.",                                                                      "REQ-LEGAL-008"),
        new("legal.privacy.current_version",           "legal",          "string",  "v1",   "Current Privacy Policy version identifier.",                                                                        "REQ-LEGAL-008"),
        new("legal.tester_acknowledgement.current_version","legal",      "string",  "v1",   "Current tester acknowledgement version identifier.",                                                                "REQ-LEGAL-004"),
        new("legal.disclaimer.long_version",           "legal",          "string",  "v1",   "Current long-form disclaimer version identifier.",                                                                  "REQ-LEGAL-007"),

        // ── Per-deployment keys ─────────────────────────────────────────

        // REQ-LEGAL-007: seeded per deployment with environment-specific values
        new("legal.disclaimer.short_text",             "legal",          "string",  "",     "Short-form disclaimer text displayed on every user-facing actionable surface.",   "REQ-LEGAL-007",         IsPerDeployment: true),
        new("legal.disclaimer.long_url",               "legal",          "string",  "",     "URL to the long-form disclaimer document.",                                                                         "REQ-LEGAL-007",         IsPerDeployment: true),

        // REQ-ORDER-010c: seeded per deployment
        new("orders.execution_assistance_mode_updated_at","orders",      "string",  "",     "UTC timestamp of the most recent execution-assistance mode transition.",                                            "REQ-ORDER-010c",        IsPerDeployment: true),

        // REQ-NOTIFY-020: seeded per deployment
        new("notifications.email.sender_domain",       "notifications",  "string",  "",     "Sender domain for platform notification emails.",                                                                   "REQ-NOTIFY-020",        IsPerDeployment: true),
        new("notifications.email.sender_address",      "notifications",  "string",  "",     "Sender address for platform notification emails.",                                                                  "REQ-NOTIFY-020",        IsPerDeployment: true),

        // ── Sentinel (written on every run, not strictly per-deployment) ──
        // platform.seed.version is handled specially by the seeder (upserted each run)
    ];
}
