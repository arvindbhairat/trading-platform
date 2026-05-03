namespace SignalStack.Worker.Rme;

/// <summary>
/// Reads the user's equity base with a snapshot-consistent protocol
/// (REQ-RME-006d) and optional manual override support (REQ-RME-006c).
///
/// The equity base is the denominator used by all RME position-sizing
/// and portfolio-heat calculations (REQ-RME-006).
/// </summary>
public interface IEquityBaseReader
{
    /// <summary>
    /// Reads the user's equity base with the V1/V2 snapshot-consistency
    /// protocol. Returns the resolved equity value and metadata.
    ///
    /// When a manual override is active (REQ-RME-006c), the returned
    /// <see cref="EquityBaseResult.EquityBase"/> is the override value;
    /// the auto-derived platform-visible equity is still available in
    /// <see cref="EquityBaseResult.PlatformVisibleEquity"/> for comparison.
    /// </summary>
    Task<EquityBaseResult> GetEquityBaseAsync(
        string userId,
        CancellationToken ct = default);
}
