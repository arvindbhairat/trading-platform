namespace SignalStack.Worker.Rme;

/// <summary>
/// Pluggable stop-loss contract. Each concrete stop type receives the entry
/// price and type-specific parameters, then computes a stop price.
/// REQ-RME-013, REQ-RME-014.
/// All models are stateless and must be registered as singletons.
/// </summary>
public interface IStopLoss
{
    /// <summary>Short, stable identifier used in configuration and audit output.</summary>
    string Name { get; }

    /// <summary>
    /// Compute a stop-loss price from the given inputs.
    /// Must not perform I/O; all data required for the calculation is supplied
    /// in <paramref name="input"/>.
    /// </summary>
    StopLossResult Calculate(StopLossInput input);
}

/// <summary>
/// Standard inputs consumed by every stop-loss model.
/// Type-specific parameters (ATR value, swing-low price, trailing percent, etc.)
/// are carried via <see cref="AdditionalInputs"/>.
/// </summary>
public sealed record StopLossInput(
    decimal EntryPrice,
    decimal? AtrValue = null,
    decimal? SwingLowPrice = null,
    decimal? TrailingStopPercent = null,
    IReadOnlyDictionary<string, object>? AdditionalInputs = null
);

/// <summary>
/// Output produced by a stop-loss calculation.
/// </summary>
public sealed record StopLossResult(
    decimal StopPrice,
    string StopTypeUsed
);
