namespace SignalStack.Worker.Rme;

/// <summary>
/// Synchronous EOD trailing-stop recalculation service.
///
/// ADR-0003 CARVE-OUT: trailing-stop updates must NOT be routed through the
/// per-position channel. Instead, this service applies updates synchronously
/// before LMDS/LADS pollers start for the new session. The per-position channel
/// consumer rejects <see cref="TrailingStopUpdateEvent"/> with an error log
/// as a safety net.
///
/// Concrete implementations are provided by:
/// - P6-T14: Trailing Stop (percentage-based)
/// - P6-T15: Trailing Stop (ATR-based) with EOD carve-out per ADR-0003
/// REQ-RME-CONC-005: EOD trailing-stop wired as event producer.
/// </summary>
public interface ITrailingStopSyncService
{
    /// <summary>
    /// Evaluates all Open positions and recalculates trailing stop levels
    /// using the configured algorithm. Runs synchronously (not through the
    /// per-position channel) per ADR-0003.
    /// </summary>
    Task RecalculateAllAsync(CancellationToken ct = default);

    /// <summary>
    /// Recalculates the trailing stop for a single position.
    /// Used by LMDS for circuit-clear re-evaluation (REQ-PLC-004).
    /// </summary>
    Task RecalculateForPositionAsync(Guid positionId, CancellationToken ct = default);
}
