namespace SignalStack.Api.Notifications;

/// <summary>
/// Supported notification type identifiers.
/// User-facing types per REQ-NOTIFY-006.
/// </summary>
public static class NotificationType
{
    // ── User-facing notification types (REQ-NOTIFY-006) ────────────────────

    public const string EntrySignal = "entry_signal";
    public const string ExitAlert = "exit_alert";
    public const string AddAdvisory = "add_advisory";
    public const string ReduceAdvisory = "reduce_advisory";
    public const string PortfolioImpactInsight = "portfolio_impact_insight";
    public const string DrawdownModeAlert = "drawdown_mode_alert";
    public const string PortfolioHeatWarning = "portfolio_heat_warning";
    public const string GapRiskAlert = "gap_risk_alert";
    public const string CircuitLimitAlert = "circuit_limit_alert";
    public const string CorporateActionWarning = "corporate_action_warning";
    public const string PendingEntrySuperseded = "pending_entry_superseded";
    public const string PendingEntryExpired = "pending_entry_expired";
    public const string SectorExposureBreach = "sector_exposure_breach";
    public const string MarketHaltActive = "market_halt_active";
    public const string MarketHaltCleared = "market_halt_cleared";

    // ── Admin-only operational notification types (REQ-NOTIFY-006a) ────────

    public const string AdminDataSyncJobFailure = "admin_data_sync_job_failure";
    public const string AdminEodSignalRunnerFailure = "admin_eod_signal_runner_failure";
    public const string AdminKillSwitchChanged = "admin_kill_switch_changed";
    public const string AdminHdsJobFailure = "admin_hds_job_failure";
    public const string AdminLmdsSuspended = "admin_lmds_suspended";
    public const string AdminLadsThresholdBreach = "admin_lads_threshold_breach";
    public const string AdminNdjThresholdBreach = "admin_ndj_threshold_breach";
    public const string AdminFyersTokenExpiryWarning = "admin_fyers_token_expiry_warning";
    public const string AdminSharedIngestionTokenLost = "admin_shared_ingestion_token_lost";
    public const string AdminTelegramBotHealth = "admin_telegram_bot_health";
    public const string AdminTelegramTokenRotationDue = "admin_telegram_token_rotation_due";
    public const string AdminTelegramGlobalDisableChanged = "admin_telegram_global_disable_changed";
    public const string AdminSysConfigChanged = "admin_sys_config_changed";
    public const string AdminPhaseTransition = "admin_phase_transition";
    public const string AdminMarketHaltDetected = "admin_market_halt_detected";
    public const string AdminMarketHaltCleared = "admin_market_halt_cleared";
    public const string AdminDataQualityBreach = "admin_data_quality_breach";
    public const string AdminFyersBudgetThreshold = "admin_fyers_budget_threshold";
    public const string AdminCorporateActionDiscontinuity = "admin_corporate_action_discontinuity";
    public const string AdminManualAdjustmentReconciliation = "admin_manual_adjustment_reconciliation";
    public const string AdminSecurityEvent = "admin_security_event";
    public const string AdminDsarRequestReceived = "admin_dsar_request_received";

    /// <summary>All valid user-facing notification types.</summary>
    public static readonly HashSet<string> UserFacing = new(StringComparer.Ordinal)
    {
        EntrySignal, ExitAlert, AddAdvisory, ReduceAdvisory,
        PortfolioImpactInsight, DrawdownModeAlert, PortfolioHeatWarning,
        GapRiskAlert, CircuitLimitAlert, CorporateActionWarning,
        PendingEntrySuperseded, PendingEntryExpired, SectorExposureBreach,
        MarketHaltActive, MarketHaltCleared,
    };

    /// <summary>All valid admin-only notification types.</summary>
    public static readonly HashSet<string> AdminOnly = new(StringComparer.Ordinal)
    {
        AdminDataSyncJobFailure, AdminEodSignalRunnerFailure, AdminKillSwitchChanged,
        AdminHdsJobFailure, AdminLmdsSuspended, AdminLadsThresholdBreach,
        AdminNdjThresholdBreach, AdminFyersTokenExpiryWarning, AdminSharedIngestionTokenLost, AdminTelegramBotHealth,
        AdminTelegramTokenRotationDue, AdminTelegramGlobalDisableChanged,
        AdminSysConfigChanged, AdminPhaseTransition, AdminMarketHaltDetected,
        AdminMarketHaltCleared, AdminDataQualityBreach, AdminFyersBudgetThreshold,
        AdminCorporateActionDiscontinuity, AdminManualAdjustmentReconciliation,
        AdminSecurityEvent, AdminDsarRequestReceived,
    };

    /// <summary>All valid notification types (user-facing + admin-only).</summary>
    public static readonly HashSet<string> All = new(
        UserFacing.Concat(AdminOnly), StringComparer.Ordinal);

    /// <summary>
    /// Non-suppressible (critical) user-facing types per REQ-PROFILE-006.
    /// These must always be delivered to Telegram regardless of user preference,
    /// and are counted as critical in the portal unread indicator.
    /// </summary>
    public static readonly HashSet<string> Critical = new(StringComparer.Ordinal)
    {
        ExitAlert, GapRiskAlert, CircuitLimitAlert,
        MarketHaltActive, MarketHaltCleared, PendingEntrySuperseded,
    };

    /// <summary>Returns true when <paramref name="type"/> is a known notification type.</summary>
    public static bool IsValid(string type) => All.Contains(type);
}
