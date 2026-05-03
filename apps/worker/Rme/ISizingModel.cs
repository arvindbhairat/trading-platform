namespace SignalStack.Worker.Rme;

/// <summary>
/// Pluggable sizing model contract. Each concrete model receives standard inputs
/// and produces a recommended position size. REQ-SIZING-001, REQ-SIZING-004.
/// All models are stateless: an instance may be used concurrently from multiple
/// positions and must be registered as a singleton.
/// </summary>
public interface ISizingModel
{
    /// <summary>Short, stable identifier used in configuration and audit output.</summary>
    string Name { get; }

    /// <summary>
    /// Compute a recommended position size from the given inputs.
    /// Must not perform I/O; all data required for the calculation is supplied
    /// in <paramref name="input"/>.
    /// </summary>
    SizingResult Calculate(SizingInput input);
}

/// <summary>
/// Standard inputs consumed by every sizing model.
/// Extended by concrete models that need additional data (ATR value, etc.)
/// via <see cref="AdditionalInputs"/>.
/// </summary>
public sealed record SizingInput(
    decimal EntryPrice,
    decimal StopPrice,
    decimal AccountEquity,
    decimal AvailableCapital,
    IReadOnlyDictionary<string, object>? AdditionalInputs = null
);

/// <summary>
/// Output produced by a sizing model. Carries enough context for the caller
/// to explain the recommendation — model identity, inputs used, derived values.
/// REQ-SIZING-009.
/// </summary>
public sealed record SizingResult(
    decimal RecommendedQuantity,
    decimal RiskAmount,
    string ModelUsed,
    string? AdvisoryMessage = null
);
