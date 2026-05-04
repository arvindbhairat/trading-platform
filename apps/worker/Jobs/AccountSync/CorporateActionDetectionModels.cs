namespace SignalStack.Worker.Jobs.AccountSync;

/// <summary>
/// A single holding reported by FYERS for a user, used as input to
/// the corporate action discontinuity detection (REQ-PORT-016).
/// </summary>
public sealed record FyersHolding
{
    /// <summary>NSE trading symbol (e.g. "RELIANCE").</summary>
    public required string Symbol { get; init; }

    /// <summary>Quantity held per FYERS.</summary>
    public required decimal Quantity { get; init; }

    /// <summary>Average cost per unit per FYERS (derived from holding value).</summary>
    public required decimal AvgCost { get; init; }
}

/// <summary>
/// A detected corporate action discontinuity between FYERS-reported
/// and FIFO-derived position state (REQ-PORT-016).
/// </summary>
public sealed record CorporateActionDiscontinuity
{
    /// <summary>NSE trading symbol.</summary>
    public required string Symbol { get; init; }

    /// <summary>Quantity reported by FYERS.</summary>
    public required decimal FyersQuantity { get; init; }

    /// <summary>Average cost per unit per FYERS.</summary>
    public required decimal FyersAvgCost { get; init; }

    /// <summary>Quantity derived from FIFO trade ledger.</summary>
    public required decimal FifoQuantity { get; init; }

    /// <summary>Average buy price derived from FIFO trade ledger.</summary>
    public required decimal FifoAvgCost { get; init; }

    /// <summary>
    /// Absolute percentage difference between FYERS and FIFO average cost.
    /// Computed as |fyersAvgCost - fifoAvgCost| / fifoAvgCost * 100.
    /// </summary>
    public required decimal AvgCostDeltaPercent { get; init; }

    /// <summary>
    /// True when condition (a) of REQ-PORT-016 is met:
    /// FYERS quantity differs from FIFO quantity by any non-zero amount.
    /// </summary>
    public bool HasQuantityMismatch => FyersQuantity != FifoQuantity;

    /// <summary>
    /// True when condition (b) of REQ-PORT-016 is met:
    /// average cost delta exceeds the configured threshold.
    /// </summary>
    public bool HasCostDeltaThreshold { get; init; }
}
