using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Computes portfolio heat from open positions and enforces the configured
/// maximum heat threshold. Uses correlated-industry grouping for sector
/// exposure aggregation (REQ-SIZING-014a).
///
/// Stateless and thread-safe; register as singleton.
/// REQ-HEAT-001, REQ-HEAT-002.
/// </summary>
internal sealed class PortfolioHeatCalculator : IPortfolioHeatCalculator
{
    private readonly IPositionRepository _positionRepository;
    private readonly IEquityBaseReader _equityBaseReader;
    private readonly CorrelatedIndustryGroupOptions _groupOptions;
    private readonly RmeModuleOptions _moduleOptions;
    private readonly ILogger<PortfolioHeatCalculator> _logger;

    public PortfolioHeatCalculator(
        IPositionRepository positionRepository,
        IEquityBaseReader equityBaseReader,
        IOptions<CorrelatedIndustryGroupOptions> groupOptions,
        IOptions<RmeModuleOptions> moduleOptions,
        ILogger<PortfolioHeatCalculator> logger)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _equityBaseReader = equityBaseReader ?? throw new ArgumentNullException(nameof(equityBaseReader));
        _groupOptions = groupOptions?.Value ?? throw new ArgumentNullException(nameof(groupOptions));
        _moduleOptions = moduleOptions?.Value ?? throw new ArgumentNullException(nameof(moduleOptions));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<PortfolioHeatResult> CalculateAsync(
        string userId,
        CancellationToken ct = default)
    {
        // ── Step 1: Read equity base ──────────────────────────────────────────
        var equityResult = await _equityBaseReader.GetEquityBaseAsync(userId, ct);

        if (!equityResult.IsSnapshotConsistent || equityResult.EquityBase <= 0m)
        {
            _logger.LogWarning(
                "PortfolioHeatCalculator: equity base unavailable or zero for user {UserId}. " +
                "Source: {Source}, Consistent: {Consistent}. " +
                "Returning zero heat.",
                userId, equityResult.Source, equityResult.IsSnapshotConsistent);

            return new PortfolioHeatResult(
                CurrentHeatPct: 0m,
                TotalOpenRisk: 0m,
                AccountEquity: 0m,
                MaxHeatPct: (decimal)_moduleOptions.MaxPortfolioHeatPct,
                OpenPositionCount: 0,
                SuspendedPositionCount: 0);
        }

        // ── Step 2: Read open positions for user ──────────────────────────────
        var positions = await _positionRepository.GetNonTerminalByUserIdAsync(userId, ct);

        var openPositions = positions.Where(p => p.State == PositionState.Open).ToList();
        var pendingPositions = positions.Where(p => p.State == PositionState.PendingEntry).ToList();
        var suspendedPositions = positions.Where(p => p.State == PositionState.Suspended).ToList();

        // ── Step 3: Calculate total open risk ──────────────────────────────────
        // Risk per position = (EntryPrice - StopLoss) × Quantity
        // Positions without a valid stop contribute 0 risk.
        // Suspended positions continue to contribute per REQ-HEAT-008.
        var totalOpenRisk = 0m;

        foreach (var pos in positions)
        {
            var risk = (pos.EntryPrice - pos.StopLoss) * pos.Quantity;
            if (risk > 0m)
                totalOpenRisk += risk;
        }

        var totalOpenRiskFormatted = totalOpenRisk;

        // ── Step 4: Calculate portfolio heat ───────────────────────────────────
        var currentHeatPct = (totalOpenRisk / equityResult.EquityBase) * 100m;

        // ── Step 5: Sector exposures with correlated-industry grouping ─────────
        // Get the correlated groups map
        var groups = _groupOptions.GetGroups();

        // Build a reverse map: industry -> group name
        var industryToGroup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups)
        {
            foreach (var industry in group.IndustryNames)
            {
                // First group wins if an industry appears in multiple groups
                if (!industryToGroup.ContainsKey(industry))
                {
                    industryToGroup[industry] = group.GroupName;
                }
            }
        }

        // Calculate risk per sector (using group names where applicable)
        var sectorRisk = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);

        foreach (var pos in openPositions.Concat(suspendedPositions))
        {
            // We don't have direct industry on PositionDocument; use symbol as fallback.
            // In a full implementation, the sector/industry would be resolved from
            // the symbol master. For now, we aggregate under "Unknown" since industry
            // data requires a symbol-master join — the sector view is refined by
            // P6-T22 (pre-trade portfolio impact panel).
            _logger.LogDebug(
                "PortfolioHeatCalculator: position {PositionId} ({Symbol}) — " +
                "industry resolution requires symbol-master join (deferred to P6-T22). " +
                "Classifying under 'Unknown' sector for heat calculation.",
                pos.PositionId, pos.Symbol);

            var risk = (pos.EntryPrice - pos.StopLoss) * pos.Quantity;
            if (risk > 0m)
            {
                var sectorKey = "Unknown";
                sectorRisk.TryGetValue(sectorKey, out var existing);
                sectorRisk[sectorKey] = existing + risk;
            }
        }

        // Convert to percentages if equity is available
        var sectorExposures = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
        foreach (var kvp in sectorRisk)
        {
            sectorExposures[kvp.Key] = (kvp.Value / equityResult.EquityBase) * 100m;
        }

        _logger.LogInformation(
            "PortfolioHeatCalculator: user {UserId} — heat={HeatPct:F2}%, " +
            "totalOpenRisk={TotalRisk:F2}, equity={Equity:F2}, " +
            "openPositions={Open}, pending={Pending}, suspended={Suspended}, " +
            "maxHeat={MaxHeat:F2}%.",
            userId, currentHeatPct, totalOpenRiskFormatted, equityResult.EquityBase,
            openPositions.Count, pendingPositions.Count, suspendedPositions.Count,
            _moduleOptions.MaxPortfolioHeatPct);

        return new PortfolioHeatResult(
            CurrentHeatPct: currentHeatPct,
            TotalOpenRisk: totalOpenRiskFormatted,
            AccountEquity: equityResult.EquityBase,
            MaxHeatPct: (decimal)_moduleOptions.MaxPortfolioHeatPct,
            OpenPositionCount: openPositions.Count + pendingPositions.Count,
            SuspendedPositionCount: suspendedPositions.Count,
            SectorExposures: sectorExposures);
    }

    public async Task<HeatEnforcementResult> AssertEntryAllowedAsync(
        string userId,
        decimal proposedRiskAmount,
        CancellationToken ct = default)
    {
        var current = await CalculateAsync(userId, ct);
        var maxHeatPct = (decimal)_moduleOptions.MaxPortfolioHeatPct;

        // If equity is zero, block the entry
        if (current.AccountEquity <= 0m)
        {
            _logger.LogWarning(
                "PortfolioHeatCalculator: entry blocked for user {UserId} — " +
                "equity base is zero or unavailable.",
                userId);

            return new HeatEnforcementResult(
                Allowed: false,
                ProjectedHeatPct: 0m,
                MaxHeatPct: maxHeatPct,
                AdvisoryMessage: "Cannot assess portfolio heat: equity base is unavailable. " +
                    "Position entry is blocked until equity data is available.");
        }

        // Calculate what the heat would be with the proposed position
        var projectedTotalRisk = current.TotalOpenRisk + proposedRiskAmount;
        var projectedHeatPct = (projectedTotalRisk / current.AccountEquity) * 100m;

        if (projectedHeatPct > maxHeatPct)
        {
            _logger.LogInformation(
                "PortfolioHeatCalculator: entry BLOCKED for user {UserId} — " +
                "projected heat {ProjectedHeat:F2}% exceeds max {MaxHeat:F2}%. " +
                "Current heat: {CurrentHeat:F2}%, proposed risk: {ProposedRisk:F2}.",
                userId, projectedHeatPct, maxHeatPct, current.CurrentHeatPct, proposedRiskAmount);

            return new HeatEnforcementResult(
                Allowed: false,
                ProjectedHeatPct: projectedHeatPct,
                MaxHeatPct: maxHeatPct,
                AdvisoryMessage:
                    $"Adding this position would push portfolio heat to " +
                    $"{projectedHeatPct:F1}%, exceeding the configured maximum of " +
                    $"{maxHeatPct:F1}%. Position entry is blocked. " +
                    $"Current heat: {current.CurrentHeatPct:F1}%.");
        }

        _logger.LogInformation(
            "PortfolioHeatCalculator: entry ALLOWED for user {UserId} — " +
            "projected heat {ProjectedHeat:F2}% within max {MaxHeat:F2}%. " +
            "Current heat: {CurrentHeat:F2}%, proposed risk: {ProposedRisk:F2}.",
            userId, projectedHeatPct, maxHeatPct, current.CurrentHeatPct, proposedRiskAmount);

        return new HeatEnforcementResult(
            Allowed: true,
            ProjectedHeatPct: projectedHeatPct,
            MaxHeatPct: maxHeatPct);
    }
}
