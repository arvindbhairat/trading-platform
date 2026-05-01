using Microsoft.Extensions.Logging;

namespace SignalStack.Api.Universe;

/// <summary>
/// Stub implementation of <see cref="ISymbolProbeClient"/> that always returns
/// <see cref="ProbeOutcome.Success"/>. Used until the MDP adapter is wired in
/// P3-T8/T9.
/// </summary>
internal sealed class StubSymbolProbeClient : ISymbolProbeClient
{
    private readonly ILogger<StubSymbolProbeClient> _logger;

    public StubSymbolProbeClient(ILogger<StubSymbolProbeClient> logger)
    {
        _logger = logger;
    }

    public Task<ProbeResult> ProbeAsync(string symbol, CancellationToken ct = default)
    {
        _logger.LogDebug("SymbolProbeClient (stub): {Symbol} → Success", symbol);
        return Task.FromResult(new ProbeResult(ProbeOutcome.Success));
    }
}
