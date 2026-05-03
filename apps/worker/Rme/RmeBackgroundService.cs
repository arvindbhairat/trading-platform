using Microsoft.Extensions.Options;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Skeleton background service for the RME module.
///
/// Current implementation is a no-op placeholder. In later Phase 6 tasks this
/// service will host:
/// - P6-T24: Nightly BTE-vs-RME parity check workflow.
/// - Periodic channel-registry capacity / idle-suspended health checks.
/// - RME profile optimisation backtest runner coordination (P6-T23).
/// </summary>
internal sealed class RmeBackgroundService : BackgroundService
{
    private readonly ILogger<RmeBackgroundService> _logger;
    private readonly IOptions<RmeModuleOptions> _options;

    public RmeBackgroundService(
        ILogger<RmeBackgroundService> logger,
        IOptions<RmeModuleOptions> options)
    {
        _logger = logger;
        _options = options;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "RME module started. MaxActivePositions: {Max}, Risk band: {Min}%–{MaxPct}%",
            _options.Value.MaxActivePositions,
            _options.Value.MinRiskPerTradePct,
            _options.Value.MaxRiskPerTradePct);

        // Placeholder: no recurring work yet. The service remains registered
        // so DI wiring and startup logging are exercised. Real cycles are
        // added by subsequent P6 tasks.
        await Task.CompletedTask;
    }
}
