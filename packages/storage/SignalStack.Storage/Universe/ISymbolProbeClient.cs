namespace SignalStack.Storage.Universe;

/// <summary>
/// Outcome classification for a single-symbol validity probe. REQ-UNIV-021a.
/// </summary>
public enum ProbeOutcome
{
    /// <summary>A valid LTP response was received.</summary>
    Success,
    /// <summary>Network failure, rate-limit response, or 5xx after retries exhausted.</summary>
    Transient,
    /// <summary>The provider indicates the symbol is not recognised.</summary>
    UnknownSymbol
}

/// <summary>
/// Result of probing a single symbol through the active market-data provider.
/// </summary>
public sealed record ProbeResult(
    ProbeOutcome Outcome,
    string? RawErrorCode = null);

/// <summary>
/// Abstracts the per-symbol LTP fetch used by the Symbol Validity Probe.
/// The real implementation is wired via the MDP adapter (P3-T8/T9); the initial
/// implementation is <see cref="StubSymbolProbeClient"/> which always succeeds.
/// REQ-UNIV-021/021a.
/// </summary>
public interface ISymbolProbeClient
{
    /// <summary>Probes a single symbol and returns the outcome classification.</summary>
    Task<ProbeResult> ProbeAsync(string symbol, CancellationToken ct = default);
}
