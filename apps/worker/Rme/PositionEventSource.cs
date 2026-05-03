namespace SignalStack.Worker.Rme;

/// <summary>
/// Exhaustive set of authorised producers that may initiate a position
/// state transition. REQ-PLC-005a, REQ-PLC-002a.
/// </summary>
public enum PositionEventSource
{
    /// <summary>Live Account Data Sync — trade fill confirmation.</summary>
    LADS,

    /// <summary>Platform system — time-based expiry, signal supersede.</summary>
    System,

    /// <summary>Admin portal action or runtime enforcement.</summary>
    ADMIN,

    /// <summary>Live Market Data Scan — price-level breach detection.</summary>
    LMDS,

    /// <summary>RME OCC retry exhaustion — concurrency conflict.</summary>
    RME_OCC,

    /// <summary>Corporate action detection — position discontinuity.</summary>
    CORPORATE_ACTION,

    /// <summary>User-initiated exit via FYERS widget.</summary>
    User
}
