namespace SignalStack.Worker.Rme;

/// <summary>
/// Computes the current portfolio heat for a user and enforces the configured
/// maximum portfolio heat threshold. REQ-HEAT-001, REQ-HEAT-002.
///
/// Portfolio Heat = Total Open Risk / Account Equity, expressed as a percentage.
/// Total Open Risk is the sum of (EntryPrice - StopLoss) × Quantity across all
/// non-terminal positions (Open, PendingEntry, Suspended per REQ-HEAT-008).
/// </summary>
public interface IPortfolioHeatCalculator
{
    /// <summary>
    /// Calculate the current portfolio heat for the given user, including
    /// sector-exposure breakdown with correlated-industry grouping.
    /// </summary>
    Task<PortfolioHeatResult> CalculateAsync(
        string userId,
        CancellationToken ct = default);

    /// <summary>
    /// Assert that adding a proposed position with the given risk amount would
    /// not push portfolio heat above the configured maximum.
    /// Returns <c>true</c> when the entry is within limits; <c>false</c> with
    /// an advisory message when the heat would be exceeded.
    /// REQ-HEAT-003.
    /// </summary>
    Task<HeatEnforcementResult> AssertEntryAllowedAsync(
        string userId,
        decimal proposedRiskAmount,
        CancellationToken ct = default);
}

/// <summary>
/// Current portfolio heat state for a user. All percentages are expressed as
/// decimal values (e.g., 2.5 = 2.5%).
/// </summary>
public sealed record PortfolioHeatResult(
    decimal CurrentHeatPct,
    decimal TotalOpenRisk,
    decimal AccountEquity,
    decimal MaxHeatPct,
    int OpenPositionCount,
    int SuspendedPositionCount,
    IReadOnlyDictionary<string, decimal>? SectorExposures = null
);

/// <summary>
/// Result of a heat-gate enforcement check. Indicates whether a proposed
/// position entry is within the configured portfolio heat limit.
/// </summary>
public sealed record HeatEnforcementResult(
    bool Allowed,
    decimal ProjectedHeatPct,
    decimal MaxHeatPct,
    string? AdvisoryMessage = null
);
