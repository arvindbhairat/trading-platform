namespace SignalStack.Worker.Rme;

/// <summary>
/// Validates position state transitions against the authoritative
/// state-transition matrix (REQ-PLC-002a). Immutable and thread-safe.
/// </summary>
public interface ITransitionValidator
{
    /// <summary>
    /// Attempt to transition from <paramref name="currentState"/> using the given
    /// <paramref name="source"/> and optional <paramref name="reasonCode"/>.
    /// </summary>
    /// <param name="currentState">The position's current lifecycle state.</param>
    /// <param name="source">The event source (producer).</param>
    /// <param name="reasonCode">The reason code, or null if the matrix row has no reason code.</param>
    /// <param name="transition">The matching transition when allowed.</param>
    /// <param name="rejectionReason">Human-readable explanation when denied.</param>
    /// <returns>True if the transition is permitted by the matrix.</returns>
    bool TryTransition(
        PositionState currentState,
        PositionEventSource source,
        string? reasonCode,
        out PositionTransition? transition,
        out string? rejectionReason);

    /// <summary>Returns every valid transition definition from the given state.</summary>
    IReadOnlyList<PositionTransition> GetAllowedTransitions(PositionState fromState);

    /// <summary>True when the state is Closed or Rejected (REQ-PLC-003).</summary>
    bool IsTerminal(PositionState state);
}
