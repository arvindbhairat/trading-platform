namespace SignalStack.Api.Notifications;

/// <summary>
/// Data required to build an entry-filled notification content string.
/// Maps to REQ-NOTIFY-024 fields (a) through (h).
/// </summary>
public sealed record EntryFillData(
    /// <summary>Trading symbol, e.g. "RELIANCE". Maps to REQ-NOTIFY-024(a).</summary>
    string Symbol,

    /// <summary>Exchange code, e.g. "NSE". Maps to REQ-NOTIFY-024(a).</summary>
    string ExchangeCode,

    /// <summary>Transaction side: "BUY" for Entry/Add, "SELL" for Reduce/Exit. Maps to REQ-NOTIFY-024(b).</summary>
    string Side,

    /// <summary>Filled quantity as confirmed by LADS. Maps to REQ-NOTIFY-024(c).</summary>
    int FilledQuantity,

    /// <summary>
    /// Requested quantity when the fill is partial; null for full fills.
    /// When set, the template renders "Filled: {FilledQuantity} of {RequestedQuantity} units requested"
    /// per REQ-NOTIFY-024 partial-fill rule.
    /// </summary>
    int? RequestedQuantity,

    /// <summary>Weighted average fill price in INR. Maps to REQ-NOTIFY-024(d).</summary>
    decimal AverageFillPrice,

    /// <summary>Initial RME stop level for this fill in INR. Maps to REQ-NOTIFY-024(e).</summary>
    decimal StopLevel,

    /// <summary>Position risk as a percentage of account equity. Maps to REQ-NOTIFY-024(f).</summary>
    decimal RiskPercent,

    /// <summary>Position risk in INR. Maps to REQ-NOTIFY-024(f).</summary>
    decimal RiskAmount,

    /// <summary>
    /// Intent source: "Signal: {signal_subscription_name}" or "Manual".
    /// Maps to REQ-NOTIFY-024(g).
    /// </summary>
    string IntentSource,

    /// <summary>Portal deep link to the affected symbol's chart page. Maps to REQ-NOTIFY-024(h).</summary>
    string DeepLink
);
