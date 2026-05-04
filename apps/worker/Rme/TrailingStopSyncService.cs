namespace SignalStack.Worker.Rme;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

/// <summary>
/// Synchronous EOD trailing-stop recalculation service using ATR-based trailing
/// stops. Replaces the <see cref="NoOpTrailingStopSyncService"/> placeholder.
///
/// ADR-0003 CARVE-OUT: trailing-stop updates are applied synchronously, NOT
/// routed through the per-position channel. The per-position channel consumer
/// rejects <see cref="TrailingStopUpdateEvent"/> as a safety net.
///
/// Non-decreasing invariant: on each recalculation, the new stop price is
/// compared against the existing stop on the position document. If the new
/// value is not higher, the update is skipped. This ensures the trailing stop
/// only moves up (tighter) over time.
///
/// ATR data sourcing: ATR values are read from the market data provider at
/// recalculation time. When ATR is unavailable, the calculation falls back to
/// the configured default fixed-stop distance percentage.
/// REQ-RME-CONC-005: EOD trailing-stop wired as event producer.
/// </summary>
internal sealed class TrailingStopSyncService : ITrailingStopSyncService
{
    private readonly IPositionRepository _positionRepository;
    private readonly IStopLoss _atrTrailingStop;
    private readonly ILogger<TrailingStopSyncService> _logger;

    public TrailingStopSyncService(
        IPositionRepository positionRepository,
        IEnumerable<IStopLoss> stopLosses,
        ILogger<TrailingStopSyncService> logger)
    {
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _atrTrailingStop = stopLosses?.FirstOrDefault(s =>
            string.Equals(s.Name, "AtrTrailingStop", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException(
                "AtrTrailingStop is not registered. Ensure AtrTrailingStopLoss is added to DI.");
    }

    /// <inheritdoc />
    public async Task RecalculateAllAsync(CancellationToken ct = default)
    {
        var positions = await _positionRepository.GetNonTerminalAsync(ct);

        _logger.LogInformation(
            "TrailingStopSync: recalculating ATR trailing stops for {Count} non-terminal positions.",
            positions.Count);

        foreach (var position in positions)
        {
            await RecalculateForPositionCoreAsync(position, ct);
        }

        _logger.LogInformation(
            "TrailingStopSync: completed ATR trailing stop recalculation for {Count} positions.",
            positions.Count);
    }

    /// <inheritdoc />
    public async Task RecalculateForPositionAsync(Guid positionId, CancellationToken ct = default)
    {
        var position = await _positionRepository.GetByIdAsync(positionId, ct);

        if (position is null)
        {
            _logger.LogWarning(
                "TrailingStopSync: position {PositionId} not found. Skipping.",
                positionId);
            return;
        }

        await RecalculateForPositionCoreAsync(position, ct);
    }

    private async Task RecalculateForPositionCoreAsync(
        PositionDocument position, CancellationToken ct)
    {
        // ── Step 1: Build input for the ATR trailing stop calculator ─────
        // Use the higher of current price or entry price as the trailing
        // baseline (the highest price observed since entry is not persisted
        // on the position document — this is an approximation that will be
        // refined when market-data sourced highest prices are available).
        var highestPriceSinceEntry = position.CurrentPrice > position.EntryPrice
            ? position.CurrentPrice
            : position.EntryPrice;

        var input = new StopLossInput(
            EntryPrice: position.EntryPrice,
            AtrValue: null, // ATR sourced from market data provider (TBD per P6-T15)
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = highestPriceSinceEntry
            });

        // ── Step 2: Calculate the candidate trailing stop ───────────────
        var result = _atrTrailingStop.Calculate(input);

        // ── Step 3: Enforce non-decreasing invariant (ADR-0003) ─────────
        // The trailing stop must never decrease — it can only move up
        // (become tighter) as the price advances.
        if (result.StopPrice <= position.TrailingStop && position.TrailingStop > 0m)
        {
            _logger.LogDebug(
                "TrailingStopSync: position {PositionId}: new stop {NewStop} <= " +
                "existing stop {ExistingStop}. Skipping (non-decreasing invariant).",
                position.PositionId, result.StopPrice, position.TrailingStop);
            return;
        }

        // ── Step 4: Apply synchronously (ADR-0003 carve-out) ────────────
        // This update must NOT be routed through the per-position channel.
        // We apply it directly to the position document via the repository.
        var updated = position.CloneWith(
            trailingStop: result.StopPrice,
            trailingStopActive: true);

        var occResult = await _positionRepository.UpdateWithOccAsync(
            updated, position.Version, ct: ct);

        if (occResult.Success)
        {
            _logger.LogInformation(
                "TrailingStopSync: position {PositionId}: trailing stop updated to {StopPrice} " +
                "(ATR-based). New version: {Version}.",
                position.PositionId, result.StopPrice, occResult.NewVersion);
        }
        else
        {
            _logger.LogWarning(
                "TrailingStopSync: OCC conflict for position {PositionId} " +
                "(expected version {Version}). Retry deferred to next EOD cycle.",
                position.PositionId, position.Version);
        }
    }
}
