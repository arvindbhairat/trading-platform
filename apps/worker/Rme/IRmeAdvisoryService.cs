namespace SignalStack.Worker.Rme;

/// <summary>
/// Coordinates RME advisory generation for a position — sizing, stop levels,
/// add/reduce levels, and portfolio impact assessment.
/// REQ-RME-004, REQ-RME-017, REQ-RME-018.
/// </summary>
public interface IRmeAdvisoryService
{
    /// <summary>
    /// Generate a pre-entry portfolio impact assessment.
    /// REQ-RME-017: projected heat, sector exposure, cash reserve, sector
    /// concentration, worst-case stop-loss scenario.
    /// </summary>
    Task<PortfolioImpactAssessment> AssessEntryImpactAsync(
        string userId,
        string symbol,
        decimal entryPrice,
        CancellationToken cancellationToken);

    /// <summary>
    /// Compute a sizing and stop advisory for a potential new position using
    /// the configured sizing model and stop type for the user's Signal Subscription.
    /// </summary>
    Task<SizingAdvisory> GetSizingRecommendationAsync(
        string userId,
        string symbol,
        decimal entryPrice,
        string sizingModelType,
        string stopLossType,
        CancellationToken cancellationToken);
}

/// <summary>
/// Projected portfolio state after a hypothetical position entry.
/// All values are advisory; no state is mutated during assessment.
/// </summary>
public sealed record PortfolioImpactAssessment(
    decimal ProjectedPortfolioHeat,
    decimal ProjectedSectorExposure,
    decimal CashReserveRemaining,
    int SameSectorPositionCount,
    decimal WorstCaseStopLossTotal
);

/// <summary>
/// Recommended position size and stop level for a potential entry.
/// </summary>
public sealed record SizingAdvisory(
    decimal RecommendedQuantity,
    decimal RiskAmount,
    decimal StopPrice,
    string SizingModelUsed,
    string StopTypeUsed,
    string? AdvisoryMessage = null
);
