using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Evaluates pyramiding (add-on) and scaling (reduce) advisory levels for open
/// positions. Enforces max add-on entries (REQ-PYR-003), progressive add-on
/// quantity reduction (REQ-PYR-004), and the never-lowers-stop floor invariant
/// (REQ-PYR-011).
///
/// All models are stateless: an instance may be used concurrently from multiple
/// positions and must be registered as a singleton.
/// REQ-PYR-001..011, REQ-SIZING-007.
/// </summary>
internal sealed class PyramidingAdvisoryService : IPyramidingAdvisoryService
{
    private readonly RmeModuleOptions _options;
    private readonly ILogger<PyramidingAdvisoryService> _logger;

    public PyramidingAdvisoryService(
        IOptions<RmeModuleOptions> options,
        ILogger<PyramidingAdvisoryService> logger)
    {
        _options = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public PyramidingAdvisory Evaluate(PyramidingEvaluationInput input)
    {
        // ── Step 1: Compute average entry price ──────────────────────────────
        var avgEntry = ComputeAverageEntryPrice(input.Tranches);

        // ── Step 2: Compute combined stop with never-lowers floor (REQ-PYR-011) ─
        // First compute the raw weighted average (no floor applied yet).
        var rawCombinedStop = ComputeRawWeightedStop(input.Tranches);

        // Then determine whether the floor should be applied.
        var stopFloorApplied = input.CurrentStopFloor.HasValue
            && input.Tranches.Count > 0
            && rawCombinedStop < input.CurrentStopFloor.Value;

        var effectiveStop = stopFloorApplied
            ? input.CurrentStopFloor!.Value
            : rawCombinedStop;

        // ── Step 3: Compute total quantity and R multiple ──────────────────────
        var totalQty = input.Tranches.Count > 0
            ? input.Tranches.Sum(t => t.Quantity)
            : 0m;

        var rMultiple = ComputeRMultiple(
            input.CurrentPrice,
            avgEntry,
            effectiveStop);

        // ── Step 4: Evaluate add trigger ───────────────────────────────────────
        var shouldAdd = false;
        var addLevel = 0;
        decimal? addPrice = null;

        if (input.RemainingAddonEntries > 0)
        {
            var addTriggerFraction = (decimal)(_options.AddTriggerMovePct / 100.0);
            var addTriggerPrice = avgEntry * (1m + addTriggerFraction);

            if (input.CurrentPrice >= addTriggerPrice)
            {
                shouldAdd = true;
                addLevel = input.Tranches.Count; // 0-based: initial entry = 0, first add = 1, etc.
                addPrice = addTriggerPrice;

                // Check whether the add-on quantity would be meaningful
                var baseQty = input.BaseQuantity ?? totalQty;
                var addonQty = ComputeAddonQuantity(
                    baseQty, addLevel, _options.AddSizeReductionPct);

                if (addonQty <= 0m)
                {
                    shouldAdd = false;
                    _logger.LogDebug(
                        "Pyramiding: add-on quantity is zero for position at add level {AddLevel}. " +
                        "Skipping add recommendation.",
                        addLevel);
                }
            }
        }

        // ── Step 5: Evaluate reduce trigger (profit target) ────────────────────
        var shouldReduce = false;
        decimal? reducePrice = null;

        // A basic reduce trigger: if R multiple >= 3, suggest a partial reduce
        if (rMultiple.HasValue && rMultiple.Value >= 3m)
        {
            shouldReduce = true;
            reducePrice = input.CurrentPrice;
        }

        // ── Step 6: Build advisory message ─────────────────────────────────────
        string? advisoryMessage = null;

        if (shouldAdd && shouldReduce)
        {
            advisoryMessage =
                $"Price ₹{input.CurrentPrice:F2} is at both add (₹{addPrice:F2}) and " +
                $"reduce (₹{reducePrice:F2}) levels. Total position: {input.Tranches.Count} tranche(s), " +
                $"{totalQty} shares. Combined stop: ₹{effectiveStop:F2}" +
                (stopFloorApplied ? " (floor applied)" : "") + ".";
        }
        else if (shouldAdd)
        {
            advisoryMessage =
                $"Price ₹{input.CurrentPrice:F2} is at add level {addLevel} " +
                $"(trigger: ₹{addPrice:F2}). {input.RemainingAddonEntries} add-on(s) remaining. " +
                $"Combined stop: ₹{effectiveStop:F2}" +
                (stopFloorApplied ? " (floor applied)" : "") +
                $". R: {(rMultiple.HasValue ? rMultiple.Value.ToString("F2") : "N/A")}.";
        }
        else if (shouldReduce)
        {
            advisoryMessage =
                $"Price ₹{input.CurrentPrice:F2} at reduce level. " +
                $"R-multiple: {rMultiple:F2}. Combined stop: ₹{effectiveStop:F2}.";
        }

        return new PyramidingAdvisory
        {
            ShouldAdd = shouldAdd,
            ShouldReduce = shouldReduce,
            AddLevel = addLevel,
            AddPrice = addPrice,
            ReducePrice = reducePrice,
            CombinedStopPrice = effectiveStop,
            AverageEntryPrice = avgEntry,
            RMultiple = rMultiple ?? 0m,
            AddonCount = input.Tranches.Count,
            MaxAddonEntries = _options.MaxPyramidingAddonEntries,
            TotalQuantity = totalQty,
            AdvisoryMessage = advisoryMessage,
            StopFloorApplied = stopFloorApplied,
            StopFloorValue = input.CurrentStopFloor,
        };
    }

    /// <summary>
    /// Compute the raw quantity-weighted average stop price across all tranches,
    /// without applying the stop floor. Used internally by <see cref="Evaluate"/>
    /// before checking the never-lowers-stop floor invariant.
    /// </summary>
    private static decimal ComputeRawWeightedStop(IReadOnlyList<TrancheRecord> tranches)
    {
        if (tranches.Count == 0)
            return 0m;

        var totalQty = tranches.Sum(t => t.Quantity);
        if (totalQty <= 0m)
            return 0m;

        var weightedStopSum = tranches.Sum(t => t.StopLoss * t.Quantity);
        return weightedStopSum / totalQty;
    }

    public decimal ComputeCombinedStop(
        IReadOnlyList<TrancheRecord> tranches,
        decimal? stopFloor = null,
        decimal? currentCombinedStop = null)
    {
        if (tranches.Count == 0)
            return 0m;

        // Quantity-weighted average of individual tranche stops
        var totalQty = tranches.Sum(t => t.Quantity);
        if (totalQty <= 0m)
            return 0m;

        var weightedStopSum = tranches.Sum(t => t.StopLoss * t.Quantity);
        var combinedStop = weightedStopSum / totalQty;

        // Never-lowers-stop floor (REQ-PYR-011):
        // If the recalculated combined stop is lower than the highest individual
        // tranche stop currently in effect, preserve the highest existing stop.
        if (stopFloor.HasValue && combinedStop < stopFloor.Value)
        {
            _logger.LogDebug(
                "Pyramiding: stop floor applied — combined stop {CombinedStop:F2} " +
                "is below floor {StopFloor:F2}. Using floor value.",
                combinedStop, stopFloor.Value);

            return stopFloor.Value;
        }

        return combinedStop;
    }

    public decimal ComputeAverageEntryPrice(IReadOnlyList<TrancheRecord> tranches)
    {
        if (tranches.Count == 0)
            return 0m;

        var totalQty = tranches.Sum(t => t.Quantity);
        if (totalQty <= 0m)
            return 0m;

        var weightedPriceSum = tranches.Sum(t => t.EntryPrice * t.Quantity);
        return weightedPriceSum / totalQty;
    }

    public decimal? ComputeRMultiple(
        decimal currentPrice,
        decimal averageEntryPrice,
        decimal combinedStopPrice)
    {
        var stopDistance = averageEntryPrice - combinedStopPrice;
        if (stopDistance <= 0m)
            return null;

        var profitPerShare = currentPrice - averageEntryPrice;
        if (profitPerShare <= 0m)
            return 0m;

        return profitPerShare / stopDistance;
    }

    public decimal ComputeAddonQuantity(decimal baseQuantity, int addLevel, double sizeReductionPct)
    {
        if (baseQuantity <= 0m || addLevel <= 0)
            return 0m;

        var reductionFactor = (decimal)(sizeReductionPct / 100.0);
        var progressiveFactor = (decimal)Math.Pow((double)(1m - reductionFactor), addLevel - 1);
        var addonQty = Math.Floor(baseQuantity * progressiveFactor);

        return addonQty > 0m ? addonQty : 0m;
    }
}
