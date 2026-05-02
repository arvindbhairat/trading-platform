using SignalStack.Api.Backtesting;

namespace SignalStack.Worker.Jobs.EodSignalRunner;

/// <summary>
/// Registry of pluggable <see cref="IEntrySignalEvaluator"/> implementations.
/// Keyed by <c>SignalTypeId</c> so EODSR can dispatch evaluation for any
/// user-subscribed Signal type without switch statements.
///
/// REQ-STRAT-025: each deployed evaluator carries a stable <c>code_version</c>.
/// </summary>
public interface ISignalEvaluatorRegistry
{
    /// <summary>Returns the evaluator for the given Signal type ID, or null if not registered.</summary>
    IEntrySignalEvaluator? GetEvaluator(string signalTypeId);

    /// <summary>Returns all registered evaluators.</summary>
    IReadOnlyCollection<IEntrySignalEvaluator> GetAll();

    /// <summary>
    /// Returns the deployed code version for the given Signal type ID.
    /// Returns null if the evaluator is not registered.
    /// </summary>
    string? GetCodeVersion(string signalTypeId);

    /// <summary>Returns a read-only map of signal_type_id -> code_version for all registrations.</summary>
    IReadOnlyDictionary<string, string> GetAllCodeVersions();
}
