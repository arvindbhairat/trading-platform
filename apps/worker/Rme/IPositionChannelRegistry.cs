namespace SignalStack.Worker.Rme;

/// <summary>
/// Singleton in-process channel registry that serialises RME event processing
/// per position. REQ-RME-CONC-001 / ADR-0003.
/// </summary>
public interface IPositionChannelRegistry
{
    /// <summary>
    /// Enqueue an event for the given position. Returns as soon as the event is
    /// written to the bounded channel (blocks if the channel is full).
    /// </summary>
    ValueTask EnqueueAsync(RmeEvent rmeEvent, CancellationToken cancellationToken = default);

    /// <summary>
    /// Create a bounded channel and start the per-position consumer task.
    /// Called when a new PendingEntry position document is written (REQ-RME-CONC-005a)
    /// and at worker startup for every non-terminal position (REQ-RME-CONC-005c).
    /// </summary>
    void OpenChannel(Guid positionId);

    /// <summary>
    /// Complete the channel writer and remove the position from the registry.
    /// The consumer task drains any remaining buffered events before terminating.
    /// Called when the position transitions to Closed or Rejected (REQ-RME-CONC-005b).
    /// </summary>
    void CloseChannel(Guid positionId);
}
