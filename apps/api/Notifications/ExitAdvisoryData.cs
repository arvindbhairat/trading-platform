namespace SignalStack.Api.Notifications;

/// <summary>
/// Data required to build an exit-advisory notification content string.
/// Maps to REQ-NOTIFY-025 fields (a) through (g).
/// </summary>
public sealed record ExitAdvisoryData(
    /// <summary>Trading symbol, e.g. "TATAMOTORS". Maps to REQ-NOTIFY-025(a).</summary>
    string Symbol,

    /// <summary>Exchange code, e.g. "NSE". Maps to REQ-NOTIFY-025(a).</summary>
    string ExchangeCode,

    /// <summary>Exit reason in human-readable form. Maps to REQ-NOTIFY-025(b).</summary>
    string ExitReason,

    /// <summary>Exit-priority rank per REQ-PLC-011. Maps to REQ-NOTIFY-025(b).</summary>
    int ExitPriority,

    /// <summary>Current market price at advisory generation time in INR. Maps to REQ-NOTIFY-025(c).</summary>
    decimal CurrentPrice,

    /// <summary>Distance from current price to active stop level in INR. Maps to REQ-NOTIFY-025(d).</summary>
    decimal DistanceToStopInr,

    /// <summary>Distance from current price to active stop level as a percentage of current price. Maps to REQ-NOTIFY-025(d).</summary>
    decimal DistanceToStopPercent,

    /// <summary>Open quantity at the time the advisory fires. Maps to REQ-NOTIFY-025(e).</summary>
    int OpenQuantity,

    /// <summary>Estimated realised PnL in INR if exited at current advisory price. Maps to REQ-NOTIFY-025(f).</summary>
    decimal EstimatedPnL,

    /// <summary>Estimated realised PnL in R multiples if exited at current advisory price. Maps to REQ-NOTIFY-025(f).</summary>
    decimal EstimatedRMultiples,

    /// <summary>Portal deep link to the affected symbol's chart page. Maps to REQ-NOTIFY-025(g).</summary>
    string DeepLink
);
