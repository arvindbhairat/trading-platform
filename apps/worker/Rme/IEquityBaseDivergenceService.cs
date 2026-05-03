namespace SignalStack.Worker.Rme;

/// <summary>
/// Assesses divergence between the FYERS total account value and the
/// platform-visible equity base, surfacing an advisory when the gap
/// exceeds the configured threshold (REQ-RME-006e).
/// </summary>
public interface IEquityBaseDivergenceService
{
    /// <summary>
    /// Evaluates the divergence ratio between FYERS total and platform-visible
    /// equity. When the absolute gap exceeds <c>risk.equity_base.external_divergence_warn_pct</c>,
    /// writes a one-time <c>equity_base_divergence_advisory</c> notification for
    /// Telegram dispatch and returns the divergence assessment.
    /// </summary>
    Task<DivergenceAssessment> AssessDivergenceAsync(
        string userId,
        EquityBaseResult equityResult,
        CancellationToken ct = default);
}

/// <summary>
/// Result of a divergence assessment — identifies whether the gap
/// exceeds the threshold and what action was taken.
/// </summary>
public sealed record DivergenceAssessment(
    /// <summary>True when <see cref="GapPercent"/> exceeds the configured threshold.</summary>
    bool HasDivergence,
    /// <summary>FYERS total account value from the equity read.</summary>
    decimal? FyersTotal,
    /// <summary>Platform-visible equity base from the equity read.</summary>
    decimal? PlatformVisible,
    /// <summary>Absolute divergence ratio as a percentage: |FYERS_total − platform_visible| / platform_visible × 100.</summary>
    decimal? GapPercent,
    /// <summary>The threshold percentage used for this assessment.</summary>
    decimal WarnThresholdPct,
    /// <summary>What action was taken as a result of this assessment.</summary>
    DivergenceAction Action);

/// <summary>
/// Describes the action taken by the divergence service.
/// </summary>
public enum DivergenceAction
{
    /// <summary>Divergence is below threshold; no action needed.</summary>
    None,
    /// <summary>Divergence exceeds threshold; an advisory notification was written.</summary>
    AdvisoryWritten,
}
