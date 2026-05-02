using SignalStack.Api.Backtesting;

namespace SignalStack.Worker.Jobs.EodSignalRunner;

/// <summary>
/// Default implementation of <see cref="ISignalEvaluatorRegistry"/>.
/// Populated via DI by registering <see cref="IEntrySignalEvaluator"/> instances.
/// </summary>
public sealed class SignalEvaluatorRegistry : ISignalEvaluatorRegistry
{
    private readonly Dictionary<string, IEntrySignalEvaluator> _evaluators;
    private readonly Dictionary<string, string> _codeVersions;

    public SignalEvaluatorRegistry(IEnumerable<IEntrySignalEvaluator> evaluators)
    {
        _evaluators = new Dictionary<string, IEntrySignalEvaluator>(StringComparer.Ordinal);

        foreach (var evaluator in evaluators)
        {
            _evaluators[evaluator.SignalTypeId] = evaluator;
        }

        _codeVersions = _evaluators
            .ToDictionary(
                kvp => kvp.Key,
                kvp => CodeVersionAttribute.GetVersion(kvp.Value),
                StringComparer.Ordinal);
    }

    public IEntrySignalEvaluator? GetEvaluator(string signalTypeId)
        => _evaluators.GetValueOrDefault(signalTypeId);

    public IReadOnlyCollection<IEntrySignalEvaluator> GetAll()
        => _evaluators.Values;

    public string? GetCodeVersion(string signalTypeId)
        => _codeVersions.GetValueOrDefault(signalTypeId);

    public IReadOnlyDictionary<string, string> GetAllCodeVersions()
        => _codeVersions;
}

/// <summary>
/// Attribute applied to <see cref="IEntrySignalEvaluator"/> implementations to
/// declare a stable code version. REQ-STRAT-025/028: provenance tracking.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CodeVersionAttribute : Attribute
{
    /// <param name="version">SemVer or date-based version string, bumped on logic change.</param>
    public CodeVersionAttribute(string version) => Version = version;

    public string Version { get; }

    /// <summary>Reads the <see cref="CodeVersionAttribute"/> from an evaluator instance.</summary>
    public static string GetVersion(IEntrySignalEvaluator evaluator)
    {
        var attr = evaluator.GetType().GetCustomAttributes(typeof(CodeVersionAttribute), false)
            .Cast<CodeVersionAttribute>()
            .FirstOrDefault();

        return attr?.Version ?? "0.0.0";
    }
}
