namespace SignalStack.Worker.Rme;

/// <summary>
/// Base for all RME events. Producers enqueue concrete subtypes into the
/// PositionChannelRegistry; the per-position consumer task processes them
/// sequentially (ADR-0003, REQ-RME-CONC-001).
/// </summary>
public abstract record RmeEvent
{
    public required Guid PositionId { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>"LMDS" | "LADS" | "EODSR" | "EOD_STOP" | "ADMIN"</summary>
    public required string Source { get; init; }
}

/// <summary>Price crossed a monitored level during an LMDS scan cycle.</summary>
public record PriceLevelBreachedEvent : RmeEvent
{
    public required LevelType Level { get; init; }
    public required decimal Price { get; init; }
}

/// <summary>A trade fill was confirmed by LADS for a PendingEntry position.</summary>
public record FillConfirmedEvent : RmeEvent
{
    public required decimal FilledQty { get; init; }
    public required decimal AvgFillPrice { get; init; }
}

/// <summary>
/// EOD trailing-stop recalculation result.
/// ADR-0003 CARVE-OUT: this event type must NOT be routed through the per-position
/// channel. The channel consumer rejects it with an error log. The EOD job must
/// apply this synchronously before LMDS/LADS pollers start for the new session.
/// </summary>
public record TrailingStopUpdateEvent : RmeEvent
{
    public required decimal NewSessionClose { get; init; }
}

/// <summary>Portfolio drawdown threshold crossed (LMDS/portfolio heat job).</summary>
public record DrawdownStateChangedEvent : RmeEvent
{
    public required DrawdownLevel Level { get; init; }
}

/// <summary>Admin kill-switch activated; all active positions must be suspended.</summary>
public record KillSwitchActivatedEvent : RmeEvent;

public enum LevelType
{
    Stop,
    Add,
    Reduce,
    TrailingStop
}

public enum DrawdownLevel
{
    None,
    Advisory,
    Breach
}
