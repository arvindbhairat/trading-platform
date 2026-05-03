namespace SignalStack.Worker.Rme;

/// <summary>
/// Canonical reason codes for position state transitions. REQ-PLC-002a.
/// Every value maps to exactly one row in the authoritative state-transition matrix.
/// Null (or empty) reason codes are used for Suspended→non-terminal resolutions
/// where the matrix specifies no reason code.
/// </summary>
public static class TransitionReason
{
    // ─── PendingEntry → Open ──────────────────────────────────────────────
    public const string EntryFillConfirmed = "entry_fill_confirmed";

    // ─── PendingEntry → Rejected ──────────────────────────────────────────
    public const string PendingEntryExpired = "pending_entry_expired";
    public const string SupersededByNewSignal = "superseded_by_new_signal";

    // ─── Open → Closed ────────────────────────────────────────────────────
    public const string StopLossHit = "stop_loss_hit";
    public const string TrailingStopHit = "trailing_stop_hit";
    public const string ManualExit = "manual_exit";
    public const string AdminForcedClose = "admin_forced_close";

    // ─── {PendingEntry, Open} → Suspended ─────────────────────────────────
    public const string KillSwitchActivated = "kill_switch_activated";
    public const string AccountDeactivated = "account_deactivated";
    public const string CircuitLimitBreach = "circuit_limit_breach";
    public const string ExtremeGapEvent = "extreme_gap_event";
    public const string ConcurrencyConflictUnresolved = "concurrency_conflict_unresolved";
    public const string CorporateActionDetected = "corporate_action_detected";
    public const string ManualAdminSuspension = "manual_admin_suspension";
    public const string UserSignalSuspended = "user_signal_suspended";
    public const string SignalTypeDisabled = "signal_type_disabled";
    public const string SubscriptionPaused = "subscription_paused";
}
