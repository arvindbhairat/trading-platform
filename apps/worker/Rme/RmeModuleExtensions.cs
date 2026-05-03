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

        // Interface contracts — no-op / not-implemented placeholders.
        // Replaced by concrete implementations as each P6 sub-task delivers.
        // P6-T8 replaces the sizing registration; P6-T12 replaces the stop
        // registration; P6-T5 wires the advisory service into the channel
        // consumer loop.
        services.AddSingleton<IRmeAdvisoryService, RmeAdvisoryService>();

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
