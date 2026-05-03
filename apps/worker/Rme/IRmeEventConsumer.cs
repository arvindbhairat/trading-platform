namespace SignalStack.Worker.Rme;

/// <summary>
/// Processes one RME event for a position. Implemented as a pure function over
/// <c>(PositionDocument, RmeEvent) → (UpdatedPositionDocument, IReadOnlyList&lt;RmeOutput&gt;)</c>
/// with no side effects (REQ-RME-CONC-003). All writes to MongoDB, <c>rme_events</c>,
/// and <c>notifications</c> are performed by the channel consumer loop after receiving
/// the output list, not inside this function.
///
/// The consumer validates the event against the authoritative state-transition matrix
/// (REQ-PLC-002a) via <see cref="ITransitionValidator"/> and returns the resulting
/// updated position document together with side-effect descriptions.
/// REQ-RME-CONC-003.
/// </summary>
public interface IRmeEventConsumer
{
    /// <summary>
    /// Transform one RME event into an updated position document and zero or more
    /// <see cref="RmeOutput"/> records describing transitions, advisory changes,
    /// and side-effect requirements.
    ///
    /// The returned <see cref="PositionDocument"/> must be a new instance (produced
    /// via <see cref="PositionDocument.CloneWith"/>) — the original must not be mutated.
    /// </summary>
    Task<(PositionDocument UpdatedPosition, IReadOnlyList<RmeOutput> Outputs)> ConsumeAsync(
        RmeEvent rmeEvent,
        PositionDocument currentPosition,
        CancellationToken cancellationToken);
}
