namespace SignalStack.Worker.Rme;

/// <summary>
/// Tracks account-level drawdown from the equity high-water mark and evaluates
/// the six graduated drawdown thresholds against the current drawdown state.
/// REQ-DRDN-001, REQ-DRDN-002, REQ-DRDN-003, REQ-DRDN-004.
/// </summary>
internal interface IDrawdownTracker
{
    /// <summary>
    /// Computes the current drawdown assessment for the given user.
    /// Evaluates all six graduated thresholds, loss-limit advisories,
    /// and returns a structured result distinguishing category (a)
    /// platform-enforced restrictions from category (b) user-advisory
    /// notifications. REQ-DRDN-003.
    /// </summary>
    Task<DrawdownAssessment> AssessAsync(string userId, CancellationToken ct = default);
}
