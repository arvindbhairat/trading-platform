namespace SignalStack.Worker.Rme;

/// <summary>
/// An immutable record of a single position state transition matching one row
/// of the authoritative state-transition matrix (REQ-PLC-002a).
/// </summary>
public sealed record PositionTransition(
    PositionState FromState,
    PositionState ToState,
    string EventDescription,
    PositionEventSource Source,
    string? ReasonCode);
