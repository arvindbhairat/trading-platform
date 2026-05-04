namespace SignalStack.Worker.Rme;

/// <summary>
/// Structured result from <see cref="IDrawdownTracker.AssessAsync"/>.
/// Carries the current drawdown percentage, loss-limit advisories, and
/// a clear split between category (a) platform-enforced restrictions
/// and category (b) user-advisory notifications. REQ-DRDN-003.
/// </summary>
internal sealed record DrawdownAssessment
{
    /// <summary>Current drawdown percentage from the peak equity high-water mark.</summary>
    public decimal CurrentDrawdownPct { get; init; }

    /// <summary>Peak equity value (high-water mark) used for drawdown computation.</summary>
    public decimal HighWaterMark { get; init; }

    /// <summary>Latest equity value from the equity curve.</summary>
    public decimal CurrentEquity { get; init; }

    /// <summary>Whether the account is at or above its high-water mark (drawdown ≤ 0).</summary>
    public bool IsAtHighWaterMark => CurrentDrawdownPct <= 0m;

    // ── Category (b) advisory: loss-limit monitoring (REQ-DRDN-002) ──────────

    /// <summary>Daily loss limit status, or null if data is insufficient.</summary>
    public LossLimitStatus? DailyLossLimit { get; init; }

    /// <summary>Weekly loss limit status, or null if data is insufficient.</summary>
    public LossLimitStatus? WeeklyLossLimit { get; init; }

    /// <summary>Monthly loss limit status, or null if data is insufficient.</summary>
    public LossLimitStatus? MonthlyLossLimit { get; init; }

    // ── Category (a): Platform-enforced restrictions on RME output ───────────

    /// <summary>
    /// 5% threshold: the RME reduces computed position sizes proportionally
    /// (REQ-DRDN-005). Applied automatically — no user acknowledgement required.
    /// This is a platform-enforced restriction on RME output (category a).
    /// </summary>
    public bool SizeReductionActive { get; init; }

    /// <summary>
    /// 10% threshold: the RME suppresses add-on entry recommendations entirely.
    /// Applied automatically — no user acknowledgement required.
    /// This is a platform-enforced restriction on RME output (category a).
    /// </summary>
    public bool AddonSuppressed { get; init; }

    /// <summary>
    /// 20% threshold: the RME suppresses new entry signal recommendations and
    /// the Phase 1 order confirmation modal rejects new entry order attempts
    /// (REQ-ORDER-004). Applied automatically — no user acknowledgement required.
    /// This is a platform-enforced restriction on RME output (category a).
    /// </summary>
    public bool EntrySuppressed { get; init; }

    // ── Category (b): User-advisory notifications ────────────────────────────

    /// <summary>
    /// 15% threshold: surface advisory to reduce all open positions.
    /// User-advisory notification (category b) — user retains full discretion.
    /// </summary>
    public bool ReducePositionsAdvisory { get; init; }

    /// <summary>
    /// 25% threshold: surface advisory to close the weakest open positions.
    /// User-advisory notification (category b) — user retains full discretion.
    /// </summary>
    public bool CloseWeakestAdvisory { get; init; }

    /// <summary>
    /// 30% threshold: surface advisory to stop all trading activity.
    /// User-advisory notification (category b) — user retains full discretion.
    /// </summary>
    public bool StopAllTradingAdvisory { get; init; }

    // ── Computed helpers ────────────────────────────────────────────────────

    /// <summary>
    /// The highest drawdown threshold level currently breached (0 = none).
    /// Levels: 1=5%, 2=10%, 3=15%, 4=20%, 5=25%, 6=30%.
    /// </summary>
    public int HighestThresholdLevel { get; init; }

    /// <summary>Human-readable summary of active advisories and enforcements.</summary>
    public string? AdvisoryMessage { get; init; }
}

/// <summary>
/// Per-period loss limit breach status. Advisory only — does not block trading.
/// REQ-DRDN-002.
/// </summary>
internal sealed record LossLimitStatus
{
    /// <summary>Period label: "daily", "weekly", or "monthly".</summary>
    public string Period { get; init; } = string.Empty;

    /// <summary>Configured loss limit as a percentage of account equity.</summary>
    public decimal LimitPct { get; init; }

    /// <summary>Current estimated loss for the period as a percentage.</summary>
    public decimal CurrentLossPct { get; init; }

    /// <summary>Whether the limit is breached.</summary>
    public bool IsBreached => CurrentLossPct > LimitPct;
}
