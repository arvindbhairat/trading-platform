namespace SignalStack.Worker.Rme;

/// <summary>
/// Source of the equity base value returned by <see cref="IEquityBaseReader"/>.
/// </summary>
public enum EquityBaseSource
{
    /// <summary>Derived from equity curve / portfolio snapshots.</summary>
    AutoDerived,
    /// <summary>User-configured manual override is in effect (REQ-RME-006c).</summary>
    ManualOverride,
    /// <summary>No equity data available — RME treats equity as zero.</summary>
    Zero,
    /// <summary>Snapshot-consistent read failed after retry exhaustion (REQ-RME-006d).</summary>
    SnapshotContentionDeferred,
}

/// <summary>
/// Result of an equity base read operation, including the resolved equity
/// value, its source, snapshot-consistency status, and the three disclosure
/// figures required by REQ-RME-006b.
/// </summary>
public sealed record EquityBaseResult
{
    /// <summary>The equity base value to use for RME sizing <em>after</em> applying
    /// any manual override. In INR.</summary>
    public required decimal EquityBase { get; init; }

    /// <summary>How <see cref="EquityBase"/> was resolved.</summary>
    public required EquityBaseSource Source { get; init; }

    // ── Snapshot-consistency (REQ-RME-006d) ──────────────────────────────

    /// <summary>True when the read was snapshot-consistent (V1 == V2).</summary>
    public required bool IsSnapshotConsistent { get; init; }

    /// <summary>Number of retry attempts made (0 = first-read success).</summary>
    public required int SnapshotRetryAttempts { get; init; }

    // ── Disclosure panel figures (REQ-RME-006b) ──────────────────────────

    /// <summary>
    /// Aggregate market value of all FYERS holdings at last known prices
    /// plus FYERS-reported available cash. Null when unavailable.
    /// </summary>
    public decimal? FyersTotalAccountValue { get; init; }

    /// <summary>
    /// Market value of holdings in platform-tracked Nifty 500 symbols
    /// at last known prices plus available cash. This is the auto-derived
    /// equity value before any override. Null when unavailable.
    /// </summary>
    public decimal? PlatformVisibleEquity { get; init; }

    /// <summary>
    /// Arithmetic difference between FYERS total and platform-visible equity.
    /// Null when either figure is unavailable.
    /// </summary>
    public decimal? ExternalHoldingsExcluded { get; init; }

    // ── Manual override metadata (REQ-RME-006c) ──────────────────────────

    /// <summary>True when a manual equity override is currently active.</summary>
    public required bool IsOverrideActive { get; init; }

    /// <summary>True when the active override has not been refreshed within
    /// the staleness window (<c>risk.equity_override_stale_days</c>).</summary>
    public required bool IsOverrideStale { get; init; }

    /// <summary>User-facing advisory message. Populated for override staleness,
    /// zero-equity, or snapshot-contention scenarios.</summary>
    public string? AdvisoryMessage { get; init; }
}
