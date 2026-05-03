namespace SignalStack.Worker.Rme;

/// <summary>
/// Formal lifecycle states for a position. REQ-PLC-002.
/// Closed and Rejected are terminal — no outgoing transitions permitted (REQ-PLC-003).
/// </summary>
public enum PositionState
{
    PendingEntry,
    Open,
    Suspended,
    Closed,
    Rejected
}
