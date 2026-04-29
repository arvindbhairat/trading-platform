namespace SignalStack.Worker.Rme;

/// <summary>
/// Processes one RME event for a position sequentially from the per-position channel
/// consumer loop. The implementation in Phase 6 (P6-T3) must be a pure function:
///   (PositionDocument, RmeEvent) → (UpdatedPositionDocument, IReadOnlyList&lt;RmeOutput&gt;)
/// with all side-effects (MongoDB writes, notifications) performed by the caller.
/// REQ-RME-CONC-003.
/// </summary>
public interface IRmeEventConsumer
{
    Task ConsumeAsync(RmeEvent rmeEvent, CancellationToken cancellationToken);
}
