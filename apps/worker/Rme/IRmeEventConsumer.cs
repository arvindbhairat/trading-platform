namespace SignalStack.Worker.Rme;

/// <summary>
/// Processes one RME event for a position sequentially from the per-position channel
/// consumer loop. The consumer validates the event against the authoritative
/// state-transition matrix (REQ-PLC-002a) via <see cref="ITransitionValidator"/>
/// and returns the resulting outputs (transition record, advisory updates,
/// side-effect requirements).
///
/// In the P6-T3 baseline, the consumer operates without direct position-document
/// access (the current state is not yet known to the consumer). P6-T4 adds OCC
/// versioning and full (PositionDocument, RmeEvent) → RmeOutput wiring.
/// REQ-RME-CONC-003.
/// </summary>
public interface IRmeEventConsumer
{
    /// <summary>
    /// Process one RME event. Returns zero or more <see cref="RmeOutput"/>
    /// records describing the transitions, advisory changes, and side-effect
    /// requirements resulting from the event.
    /// </summary>
    Task<IReadOnlyList<RmeOutput>> ConsumeAsync(
        RmeEvent rmeEvent,
        CancellationToken cancellationToken);
}
