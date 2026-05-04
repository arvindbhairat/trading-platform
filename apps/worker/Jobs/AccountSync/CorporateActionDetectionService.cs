using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Portfolio;

namespace SignalStack.Worker.Jobs.AccountSync;

/// <summary>
/// Detects corporate action discontinuities between FYERS-reported holding
/// state and FIFO-derived trade ledger state (REQ-PORT-016, REQ-PLC-010).
///
/// For each FYERS holding, the service compares quantity and average cost
/// against the FIFO-computed values from the trade ledger. When a discontinuity
/// meeting either REQ-PORT-016 condition (quantity mismatch or cost delta
/// exceeding threshold) is found, a <see cref="CorporateActionDiscontinuity"/>
/// result is produced.
///
/// The caller (LADS sync) uses these results to enqueue
/// <see cref="CorporateActionDetectedEvent"/> through the per-position
/// channel registry, triggering position suspension per REQ-PLC-010.
/// </summary>
public sealed class CorporateActionDetectionService
{
    private readonly HoldingsService _holdingsService;
    private readonly IMongoDatabase _database;
    private readonly ILogger<CorporateActionDetectionService> _logger;

    private const string AvgCostDeltaThresholdKey = "risk.corporate_action.avg_cost_delta_threshold_pct";
    private const decimal DefaultAvgCostDeltaThresholdPct = 50m;

    public CorporateActionDetectionService(
        HoldingsService holdingsService,
        IMongoDatabase database,
        ILogger<CorporateActionDetectionService> logger)
    {
        _holdingsService = holdingsService ?? throw new ArgumentNullException(nameof(holdingsService));
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Detects corporate action discontinuities for a user by comparing
    /// FYERS-reported holdings against FIFO-derived holdings.
    ///
    /// Returns a list of discontinuities (one per affected symbol) or an
    /// empty list if no discontinuities are found.
    ///
    /// REQ-PORT-016 conditions:
    ///   (a) Quantity mismatch — FYERS quantity differs from FIFO quantity
    ///       by any non-zero amount.
    ///   (b) Average cost delta — implied average cost from FYERS differs
    ///       from FIFO-derived average buy price by more than the
    ///       configured threshold percentage.
    /// </summary>
    /// <param name="userId">The user to check.</param>
    /// <param name="fyersHoldings">Holdings as reported by FYERS.</param>
    /// <param name="ct">Cancellation token.</param>
    public async Task<List<CorporateActionDiscontinuity>> DetectAsync(
        string userId,
        IReadOnlyList<FyersHolding> fyersHoldings,
        CancellationToken ct = default)
    {
        if (fyersHoldings.Count == 0)
        {
            _logger.LogTrace("CA detection: no FYERS holdings provided for user {UserId}.", userId);
            return [];
        }

        // Read the configured threshold from sys_config.
        var thresholdPct = await ReadThresholdAsync(ct);

        // Compute FIFO holdings from the trade ledger.
        var fifoHoldings = await _holdingsService.ComputeHoldingsAsync(userId, ct);
        var fifoBySymbol = fifoHoldings
            .GroupBy(h => h.Symbol)
            .ToDictionary(g => g.Key, g => g.First());

        var discontinuities = new List<CorporateActionDiscontinuity>();

        foreach (var fyersHolding in fyersHoldings)
        {
            var hasFifoHolding = fifoBySymbol.TryGetValue(fyersHolding.Symbol, out var fifoHolding);

            var fifoQuantity = hasFifoHolding ? fifoHolding!.Quantity : 0m;
            var fifoAvgCost = hasFifoHolding ? fifoHolding!.AverageBuyPrice : 0m;

            // Condition (a): quantity mismatch by any non-zero amount.
            var quantityMismatch = fyersHolding.Quantity != fifoQuantity;

            // Condition (b): average cost delta exceeding threshold.
            var avgCostDeltaPct = ComputeAvgCostDeltaPct(fifoAvgCost, fyersHolding.AvgCost);
            var costDeltaExceeded = avgCostDeltaPct >= thresholdPct;

            if (!quantityMismatch && !costDeltaExceeded)
                continue;

            _logger.LogWarning(
                "CA detection: discontinuity for user {UserId}, symbol {Symbol}. " +
                "FYERS: qty={FyersQty}, avgCost={FyersCost:F2}. " +
                "FIFO: qty={FifoQty}, avgCost={FifoCost:F2}. " +
                "Delta={DeltaPct:F1}% (threshold={Threshold}%). " +
                "QuantityMismatch={QtyMatch}, CostDeltaExceeded={CostDelta}.",
                userId, fyersHolding.Symbol,
                fyersHolding.Quantity, fyersHolding.AvgCost,
                fifoQuantity, fifoAvgCost,
                avgCostDeltaPct, thresholdPct,
                quantityMismatch, costDeltaExceeded);

            discontinuities.Add(new CorporateActionDiscontinuity
            {
                Symbol = fyersHolding.Symbol,
                FyersQuantity = fyersHolding.Quantity,
                FyersAvgCost = fyersHolding.AvgCost,
                FifoQuantity = fifoQuantity,
                FifoAvgCost = fifoAvgCost,
                AvgCostDeltaPercent = Math.Round(avgCostDeltaPct, 2),
                HasCostDeltaThreshold = costDeltaExceeded,
            });
        }

        _logger.LogInformation(
            "CA detection: found {Count} discontinuities for user {UserId} " +
            "(threshold: {Threshold}%).",
            discontinuities.Count, userId, thresholdPct);

        return discontinuities;
    }

    /// <summary>
    /// Computes the absolute percentage difference between FIFO average cost
    /// and FYERS average cost. Returns 0 when FIFO cost is 0 (no trade history).
    /// </summary>
    private static decimal ComputeAvgCostDeltaPct(decimal fifoAvgCost, decimal fyersAvgCost)
    {
        if (fifoAvgCost == 0m)
            return 0m;

        return Math.Abs((fyersAvgCost - fifoAvgCost) / fifoAvgCost) * 100m;
    }

    /// <summary>
    /// Reads the corporate action average-cost delta threshold from sys_config.
    /// Falls back to <see cref="DefaultAvgCostDeltaThresholdPct"/> (50%) if not set.
    /// </summary>
    private async Task<decimal> ReadThresholdAsync(CancellationToken ct)
    {
        try
        {
            var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
            var filter = Builders<BsonDocument>.Filter.Eq("key", AvgCostDeltaThresholdKey);
            var doc = await sysConfig.Find(filter).FirstOrDefaultAsync(ct);

            if (doc is null)
                return DefaultAvgCostDeltaThresholdPct;

            var value = doc.GetValue("value", BsonNull.Value);
            if (value.IsBsonNull || !decimal.TryParse(value.AsString, out var parsed))
                return DefaultAvgCostDeltaThresholdPct;

            return parsed;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "CA detection: failed to read {ConfigKey}. Using default {Default}.",
                AvgCostDeltaThresholdKey, DefaultAvgCostDeltaThresholdPct);
            return DefaultAvgCostDeltaThresholdPct;
        }
    }
}
