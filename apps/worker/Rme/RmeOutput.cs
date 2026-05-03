namespace SignalStack.Worker.Rme;

/// <summary>
/// Output from processing one RME event through the position lifecycle
/// state machine. Carries the resulting transition (if any), advisory flag
/// changes, and side-effect requirements.
/// </summary>
public sealed record RmeOutput
{
    /// <summary>
    /// The state transition that was applied, or null if no transition occurred
    /// (e.g. advisory-only update, rejected transition).
    /// </summary>
    public PositionTransition? Transition { get; init; }

    /// <summary>
    /// True when the transition must atomically write an incident record to
    /// <c>rme_incidents</c>. Required for every transition to Suspended
    /// state per REQ-PLC-005a, and every transition to Closed/Rejected
    /// that carries a terminal incident record.
    /// </summary>
    public bool IncidentRecordRequired { get; init; }

    /// <summary>
    /// True when the transition must write an <c>audit_events</c> record.
    /// Required for all ADMIN-source transitions per REQ-PLC-005a.
    /// </summary>
    public bool AuditRecordRequired { get; init; }

    /// <summary>Human-readable advisory message for notification dispatch.</summary>
    public string? AdvisoryMessage { get; init; }

    /// <summary>
    /// Updated advisory flags for the position. Null when no advisory flags
    /// were changed by this event.
    /// </summary>
    public AdvisoryFlagsUpdate? AdvisoryFlags { get; init; }
}

/// <summary>
/// Describes changes to the four advisory flags on a position (REQ-PLC-002).
/// Each property is non-null only when the flag was changed by the event.
/// </summary>
public sealed record AdvisoryFlagsUpdate
{
    public bool? AddAdvisoryActive { get; init; }
    public bool? ReduceAdvisoryActive { get; init; }
    public bool? TrailingStopActive { get; init; }
    public bool? ExitAdvisoryActive { get; init; }
}
