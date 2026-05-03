namespace Microsoft.Extensions.Hosting;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SignalStack.Worker.Rme;

/// <summary>
/// DI registration for the RME module (P6-T2 onward).
/// Registers the state machine (P6-T3), sizing / stop / advisory interfaces,
/// module options, and the background service. Concrete model and service
/// implementations are added by subsequent P6 tasks as they replace no-op
/// registrations.
/// REQ-RME-001, REQ-RME-012.
/// </summary>
public static class RmeModuleExtensions
{
    public static IServiceCollection AddRmeModule(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Options — validated at startup
        services
            .AddOptions<RmeModuleOptions>()
            .BindConfiguration(RmeModuleOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── P6-T3: Position lifecycle state machine ─────────────────────────
        // ITransitionValidator — immutable, thread-safe, encodes the full
        // authoritative state-transition matrix (REQ-PLC-002a).
        services.AddSingleton<ITransitionValidator, TransitionValidator>();

        // ── P6-T4: Position document OCC repository ──────────────────────────
        // IPositionRepository — _version-based Optimistic Concurrency Control
        // with fencing-token stale-write detection (REQ-RME-CONC-002, REQ-PORT-031b).
        services.AddSingleton<IPositionRepository, MongoPositionRepository>();

        // Interface contracts — no-op / not-implemented placeholders.
        // Replaced by concrete implementations as each P6 sub-task delivers.
        // P6-T8 replaces the sizing registration; P6-T12 replaces the stop
        // registration; P6-T5 wires the advisory service into the channel
        // consumer loop.
        services.AddSingleton<IRmeAdvisoryService, RmeAdvisoryService>();

        // P6-T5: Synchronous trailing-stop recalculation (ADR-0003 carve-out).
        // Replaced by concrete implementations in P6-T14/T15.
        services.AddSingleton<ITrailingStopSyncService, NoOpTrailingStopSyncService>();

        // Background service — skeleton only; real work added by P6-T24.
        services.AddHostedService<RmeBackgroundService>();

        return services;
    }
}

/// <summary>
/// Placeholder advisory service that logs and returns default (zero) advisories.
/// Replaced by a real implementation in P6-T6 (equity-base read) + P6-T8/P6-T12
/// (sizing + stop wiring).
/// </summary>
internal sealed class RmeAdvisoryService : IRmeAdvisoryService
{
    private readonly ILogger<RmeAdvisoryService> _logger;

    public RmeAdvisoryService(ILogger<RmeAdvisoryService> logger)
    {
        _logger = logger;
    }

    public Task<PortfolioImpactAssessment> AssessEntryImpactAsync(
        string userId,
        string symbol,
        decimal entryPrice,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "RME advisory service not yet implemented — returning default impact assessment "
            + "(userId: {UserId}, symbol: {Symbol}). Will be wired in P6-T6/T8/T12.",
            userId, symbol);

        var result = new PortfolioImpactAssessment(
            ProjectedPortfolioHeat: 0m,
            ProjectedSectorExposure: 0m,
            CashReserveRemaining: 0m,
            SameSectorPositionCount: 0,
            WorstCaseStopLossTotal: 0m);

        return Task.FromResult(result);
    }

    public Task<SizingAdvisory> GetSizingRecommendationAsync(
        string userId,
        string symbol,
        decimal entryPrice,
        string sizingModelType,
        string stopLossType,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "RME advisory service not yet implemented — returning default sizing advisory "
            + "(userId: {UserId}, symbol: {Symbol}). Will be wired in P6-T8/T12.",
            userId, symbol);

        var result = new SizingAdvisory(
            RecommendedQuantity: 0m,
            RiskAmount: 0m,
            StopPrice: 0m,
            SizingModelUsed: sizingModelType,
            StopTypeUsed: stopLossType,
            AdvisoryMessage: "Sizing engine not yet available.");

        return Task.FromResult(result);
    }
}

/// <summary>
/// No-op placeholder for the synchronous EOD trailing-stop recalculation service.
/// ADR-0003 carve-out: trailing-stop updates must NOT route through the per-position
/// channel. Replaced by concrete implementations in P6-T14 (percentage) and
/// P6-T15 (ATR with EOD carve-out).
/// </summary>
internal sealed class NoOpTrailingStopSyncService : ITrailingStopSyncService
{
    private readonly ILogger<NoOpTrailingStopSyncService> _logger;

    public NoOpTrailingStopSyncService(ILogger<NoOpTrailingStopSyncService> logger)
    {
        _logger = logger;
    }

    public Task RecalculateAllAsync(CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Trailing-stop recalculation not yet implemented (P6-T14/T15). " +
            "Synchronous EOD trailing-stop path is a no-op placeholder.");
        return Task.CompletedTask;
    }

    public Task RecalculateForPositionAsync(Guid positionId, CancellationToken ct = default)
    {
        _logger.LogWarning(
            "Per-position trailing-stop recalculation not yet implemented (P6-T14/T15). " +
            "Position {PositionId}: no-op placeholder.", positionId);
        return Task.CompletedTask;
    }
}
