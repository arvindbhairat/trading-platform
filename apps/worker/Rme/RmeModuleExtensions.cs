namespace Microsoft.Extensions.Hosting;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
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

        services
            .AddOptions<EquityReadOptions>()
            .BindConfiguration(EquityReadOptions.SectionName)
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

        // ── P6-T6: Snapshot-consistent equity-base reader ────────────────────
        // IEquityBaseReader — V1/V2 snapshot protocol with retry, override
        // support, and OTEL telemetry (REQ-RME-006a/b/c/d).
        services.AddSingleton<IEquityBaseReader, EquityBaseReader>();

        // ── P6-T8: Sizing models ──────────────────────────────────────────────
        // FixedPercentageSizingModel — default sizing model (REQ-SIZING-005).
        // Registered as singleton (stateless, thread-safe).
        services.AddSingleton<ISizingModel, FixedPercentageSizingModel>();

        // P6-T9: ATR-based sizing model (volatility-adjusted).
        // AtrSizingModel — computes position size from ATR and configurable multiplier.
        // (portfolio-risk-guidelines § Position Sizing Models).
        services.AddSingleton<ISizingModel, AtrSizingModel>();

        // RmeAdvisoryService — replaced by concrete implementation in P6-T6
        // (equity-base read). Full sizing + stop wiring in P6-T8/P6-T12.
        services.AddSingleton<IRmeAdvisoryService, RmeAdvisoryService>();

        // ── P6-T7: Equity-base divergence advisory ───────────────────────────
        // IEquityBaseDivergenceService — detects divergence between FYERS total
        // and platform-visible equity, writing an advisory notification when
        // the gap exceeds risk.equity_base.external_divergence_warn_pct
        // (REQ-RME-006e).
        services.AddSingleton<IEquityBaseDivergenceService, EquityBaseDivergenceService>();

        // P6-T5: Synchronous trailing-stop recalculation (ADR-0003 carve-out).
        // Replaced by concrete implementations in P6-T14/T15.
        services.AddSingleton<ITrailingStopSyncService, NoOpTrailingStopSyncService>();

        // Background service — skeleton only; real work added by P6-T24.
        services.AddHostedService<RmeBackgroundService>();

        return services;
    }
}

/// <summary>
/// RME advisory service that uses <see cref="IEquityBaseReader"/> to read
/// the user's equity base with snapshot consistency (REQ-RME-006d) and
/// dispatches to the configured <see cref="ISizingModel"/> for position sizing
/// (P6-T8). Additional models (ATR, portfolio heat, drawdown-adjusted) are
/// added in P6-T9/P6-T10/P6-T11.
/// REQ-RME-004, REQ-RME-017, REQ-RME-018.
/// </summary>
internal sealed class RmeAdvisoryService : IRmeAdvisoryService
{
    private readonly IEquityBaseReader _equityBaseReader;
    private readonly IEnumerable<ISizingModel> _sizingModels;
    private readonly ILogger<RmeAdvisoryService> _logger;

    public RmeAdvisoryService(
        IEquityBaseReader equityBaseReader,
        IEnumerable<ISizingModel> sizingModels,
        ILogger<RmeAdvisoryService> logger)
    {
        _equityBaseReader = equityBaseReader ?? throw new ArgumentNullException(nameof(equityBaseReader));
        _sizingModels = sizingModels ?? throw new ArgumentNullException(nameof(sizingModels));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PortfolioImpactAssessment> AssessEntryImpactAsync(
        string userId,
        string symbol,
        decimal entryPrice,
        CancellationToken cancellationToken)
    {
        _logger.LogWarning(
            "RME advisory service not yet fully implemented — returning default impact assessment "
            + "(userId: {UserId}, symbol: {Symbol}). Full portfolio impact wiring in P6-T22.",
            userId, symbol);

        // Read equity base with snapshot consistency (REQ-RME-006d).
        var equityResult = await _equityBaseReader.GetEquityBaseAsync(userId, cancellationToken);

        if (!equityResult.IsSnapshotConsistent)
        {
            throw new InvalidOperationException(
                $"Cannot assess entry impact: {equityResult.AdvisoryMessage ?? "snapshot contention"}. " +
                "Please retry.");
        }

        // Return a partial assessment; full implementation in P6-T22.
        var result = new PortfolioImpactAssessment(
            ProjectedPortfolioHeat: 0m,
            ProjectedSectorExposure: 0m,
            CashReserveRemaining: equityResult.EquityBase,
            SameSectorPositionCount: 0,
            WorstCaseStopLossTotal: 0m);

        return result;
    }

    public async Task<SizingAdvisory> GetSizingRecommendationAsync(
        string userId,
        string symbol,
        decimal entryPrice,
        string sizingModelType,
        string stopLossType,
        CancellationToken cancellationToken)
    {
        // ── Step 1: Read equity base with snapshot consistency (REQ-RME-006d) ──
        var equityResult = await _equityBaseReader.GetEquityBaseAsync(userId, cancellationToken);

        if (!equityResult.IsSnapshotConsistent)
        {
            _logger.LogWarning(
                "RME: sizing recommendation deferred for user {UserId} symbol {Symbol} — " +
                "{AdvisoryMessage}",
                userId, symbol, equityResult.AdvisoryMessage);

            return new SizingAdvisory(
                RecommendedQuantity: 0m,
                RiskAmount: 0m,
                StopPrice: 0m,
                SizingModelUsed: sizingModelType,
                StopTypeUsed: stopLossType,
                AdvisoryMessage: equityResult.AdvisoryMessage);
        }

        if (equityResult.Source == EquityBaseSource.Zero)
        {
            _logger.LogWarning(
                "RME: sizing blocked for user {UserId} symbol {Symbol} — equity base is zero. "
                + "{AdvisoryMessage}",
                userId, symbol, equityResult.AdvisoryMessage);

            return new SizingAdvisory(
                RecommendedQuantity: 0m,
                RiskAmount: 0m,
                StopPrice: 0m,
                SizingModelUsed: sizingModelType,
                StopTypeUsed: stopLossType,
                AdvisoryMessage: equityResult.AdvisoryMessage);
        }

        // ── Step 2: Dispatch to the configured sizing model (P6-T8) ──────────
        // Find the model by type; fall back to FixedPercentage if unknown or empty.
        var model = _sizingModels.FirstOrDefault(m =>
            string.Equals(m.Name, sizingModelType, StringComparison.OrdinalIgnoreCase))
            ?? _sizingModels.OfType<FixedPercentageSizingModel>().FirstOrDefault();

        if (model is null)
        {
            _logger.LogError(
                "RME: no sizing model found for type '{SizingModelType}' " +
                "and no FixedPercentageSizingModel registered. Returning zero advisory.",
                sizingModelType);

            return new SizingAdvisory(
                RecommendedQuantity: 0m,
                RiskAmount: 0m,
                StopPrice: 0m,
                SizingModelUsed: sizingModelType,
                StopTypeUsed: stopLossType,
                AdvisoryMessage: "Sizing model unavailable — no matching model registered.");
        }

        var sizingInput = new SizingInput(
            EntryPrice: entryPrice,
            StopPrice: 0m, // Stop price wiring in P6-T12
            AccountEquity: equityResult.EquityBase,
            AvailableCapital: equityResult.EquityBase,
            AdditionalInputs: null);

        var result = model.Calculate(sizingInput);

        _logger.LogInformation(
            "RME: sizing recommendation for user {UserId} symbol {Symbol} — " +
            "model: {ModelName}, equity base: {EquityBase}, " +
            "recommended qty: {Qty} @ price ₹{Price}. " +
            "Source: {Source}. Override active: {Override}. " +
            "Snapshot consistent: {Consistent}.",
            userId, symbol, model.Name, equityResult.EquityBase,
            result.RecommendedQuantity, entryPrice,
            equityResult.Source, equityResult.IsOverrideActive,
            equityResult.IsSnapshotConsistent);

        return new SizingAdvisory(
            RecommendedQuantity: result.RecommendedQuantity,
            RiskAmount: result.RiskAmount,
            StopPrice: 0m, // Stop price wiring in P6-T12
            SizingModelUsed: model.Name,
            StopTypeUsed: stopLossType,
            AdvisoryMessage: equityResult.AdvisoryMessage);
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
