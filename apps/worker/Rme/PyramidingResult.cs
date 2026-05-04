namespace SignalStack.Worker.Rme;

/// <summary>
/// Advisory result from the pyramiding service indicating whether an add-on,
/// reduce, or no action is recommended at the current price.
/// REQ-PYR-001, REQ-PYR-007, REQ-PYR-011.
/// </summary>
public sealed record PyramidingAdvisory
{
    /// <summary>True when an add-on (pyramid entry) is recommended at the current price.</summary>
    public required bool ShouldAdd { get; init; }

    /// <summary>True when a scale-out (reduce) is recommended at the current price.</summary>
    public required bool ShouldReduce { get; init; }

    /// <summary>
    /// The add level number. 1 = first add, 2 = second add, etc.
    /// Meaningful only when <see cref="ShouldAdd"/> is true.
    /// </summary>
    public int AddLevel { get; init; }

    /// <summary>Price that triggered the add recommendation (or null). REQ-PYR-001.</summary>
    public decimal? AddPrice { get; init; }

    /// <summary>Price that triggered the reduce recommendation (or null). REQ-PYR-007.</summary>
    public decimal? ReducePrice { get; init; }

    /// <summary>
    /// The combined stop price for the position, enforcing the never-lowers-stop
    /// floor invariant (REQ-PYR-011).
    /// </summary>
    public required decimal CombinedStopPrice { get; init; }

    /// <summary>
    /// Quantity-weighted average entry price across all tranches.
    /// REQ-PYR-010.
    /// </summary>
    public required decimal AverageEntryPrice { get; init; }

    /// <summary>Current R multiple for the combined position. REQ-PYR-010.</summary>
    public required decimal RMultiple { get; init; }

    /// <summary>Number of add-on tranches currently active. REQ-PYR-003.</summary>
    public required int AddonCount { get; init; }

    /// <summary>Configured maximum add-on entries. REQ-PYR-003.</summary>
    public required int MaxAddonEntries { get; init; }

    /// <summary>Total position quantity across all tranches.</summary>
    public required decimal TotalQuantity { get; init; }

    /// <summary>Human-readable advisory message, or null when no advisory is active.</summary>
    public string? AdvisoryMessage { get; init; }

    /// <summary>
    /// True when the never-lowers-stop floor was applied to keep the combined
    /// stop from dropping below the highest individual tranche stop.
    /// REQ-PYR-011.
    /// </summary>
    public bool StopFloorApplied { get; init; }

    /// <summary>
    /// The stop floor value that was in effect, if <see cref="StopFloorApplied"/> is true.
    /// REQ-PYR-011.
    /// </summary>
    public decimal? StopFloorValue { get; init; }
}

/// <summary>
/// Input parameters for evaluating add-on and reduce levels.
/// </summary>
public sealed record PyramidingEvaluationInput
{
    /// <summary>The current market price of the symbol.</summary>
    public required decimal CurrentPrice { get; init; }

    /// <summary>Quantity-weighted average entry price across all tranches.</summary>
    public required decimal AverageEntryPrice { get; init; }

    /// <summary>Current ATR value, or null if unavailable.</summary>
    public decimal? AtrValue { get; init; }

    /// <summary>List of existing tranches (empty for initial entry evaluation).</summary>
    public required IReadOnlyList<TrancheRecord> Tranches { get; init; }

    /// <summary>Remaining add-on entries allowed (max - count). REQ-PYR-003.</summary>
    public required int RemainingAddonEntries { get; init; }

    /// <summary>The configured percentage move from entry that triggers an add.</summary>
    public required double AddTriggerMovePct { get; init; }

    /// <summary>
    /// The stop floor — highest individual tranche stop currently in effect.
    /// REQ-PYR-011.
    /// </summary>
    public decimal? CurrentStopFloor { get; init; }

    /// <summary>The current combined stop price, or null for initial entry.</summary>
    public decimal? CurrentCombinedStop { get; init; }

    /// <summary>Phase A default base quantity used for initial entry sizing.</summary>
    public decimal? BaseQuantity { get; init; }
}
