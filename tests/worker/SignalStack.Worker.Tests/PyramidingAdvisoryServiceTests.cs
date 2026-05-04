using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the Pyramiding Advisory Service: add/reduce level computation,
/// max add-on enforcement (REQ-PYR-003), progressive add-on quantity
/// reduction (REQ-PYR-004), and never-lowers-stop floor invariant (REQ-PYR-011).
/// </summary>
public sealed class PyramidingAdvisoryServiceTests
{
    // ─── ComputeAverageEntryPrice ─────────────────────────────────────────────

    [Fact]
    public void ComputeAverageEntryPrice_returns_zero_when_no_tranches()
    {
        var service = CreateService();
        var result = service.ComputeAverageEntryPrice([]);
        Assert.Equal(0m, result);
    }

    [Fact]
    public void ComputeAverageEntryPrice_single_tranche()
    {
        var service = CreateService();
        var tranches = new List<TrancheRecord>
        {
            new() { EntryPrice = 500m, Quantity = 10m, StopLoss = 450m, EnteredAt = DateTime.UtcNow }
        };

        var result = service.ComputeAverageEntryPrice(tranches);
        Assert.Equal(500m, result);
    }

    [Fact]
    public void ComputeAverageEntryPrice_multiple_tranches()
    {
        var service = CreateService();
        var tranches = new List<TrancheRecord>
        {
            new() { EntryPrice = 500m, Quantity = 10m, StopLoss = 450m, EnteredAt = DateTime.UtcNow },
            new() { EntryPrice = 550m, Quantity = 5m, StopLoss = 500m, EnteredAt = DateTime.UtcNow },
        };

        // Weighted avg = (500*10 + 550*5) / 15 = (5000 + 2750) / 15 = 7750/15 = 516.67
        var result = service.ComputeAverageEntryPrice(tranches);
        Assert.Equal(516.67m, Math.Round(result, 2));
    }

    // ─── ComputeRMultiple ─────────────────────────────────────────────────────

    [Fact]
    public void ComputeRMultiple_returns_null_when_stop_distance_is_zero()
    {
        var service = CreateService();
        var result = service.ComputeRMultiple(550m, 500m, 500m);
        Assert.Null(result);
    }

    [Fact]
    public void ComputeRMultiple_returns_zero_when_no_profit()
    {
        var service = CreateService();
        var result = service.ComputeRMultiple(480m, 500m, 450m);
        Assert.Equal(0m, result);
    }

    [Fact]
    public void ComputeRMultiple_positive_r()
    {
        var service = CreateService();
        // stopDistance = 500 - 450 = 50
        // profit = 550 - 500 = 50
        // R = 50 / 50 = 1.0
        var result = service.ComputeRMultiple(550m, 500m, 450m);
        Assert.Equal(1.0m, result);
    }

    [Fact]
    public void ComputeRMultiple_profit_at_3r()
    {
        var service = CreateService();
        // stopDistance = 500 - 450 = 50
        // profit = 650 - 500 = 150
        // R = 150 / 50 = 3.0
        var result = service.ComputeRMultiple(650m, 500m, 450m);
        Assert.Equal(3.0m, result);
    }

    // ─── ComputeCombinedStop ──────────────────────────────────────────────────

    [Fact]
    public void ComputeCombinedStop_returns_zero_when_no_tranches()
    {
        var service = CreateService();
        var result = service.ComputeCombinedStop([]);
        Assert.Equal(0m, result);
    }

    [Fact]
    public void ComputeCombinedStop_single_tranche()
    {
        var service = CreateService();
        var tranches = new List<TrancheRecord>
        {
            new() { EntryPrice = 500m, Quantity = 10m, StopLoss = 450m, EnteredAt = DateTime.UtcNow }
        };

        var result = service.ComputeCombinedStop(tranches);
        Assert.Equal(450m, result);
    }

    [Fact]
    public void ComputeCombinedStop_weighted_average()
    {
        var service = CreateService();
        var tranches = new List<TrancheRecord>
        {
            new() { EntryPrice = 500m, Quantity = 10m, StopLoss = 450m, EnteredAt = DateTime.UtcNow },
            new() { EntryPrice = 550m, Quantity = 5m, StopLoss = 520m, EnteredAt = DateTime.UtcNow },
        };

        // Weighted avg = (450*10 + 520*5) / 15 = (4500 + 2600) / 15 = 7100/15 = 473.33
        var result = service.ComputeCombinedStop(tranches);
        Assert.Equal(473.33m, Math.Round(result, 2));
    }

    // ─── Never-lowers-stop floor (REQ-PYR-011) ─────────────────────────────────

    [Fact]
    public void ComputeCombinedStop_applies_stop_floor_when_combined_is_lower()
    {
        var service = CreateService();
        // Tranche 1: stop = 450, Tranche 2: stop = 400 (wider stop)
        // Combined average stop = (450*10 + 400*5)/15 = 433.33
        // Floor = highest individual stop = 450
        // Since combined (433.33) < floor (450), result should be floor (450)
        var tranches = new List<TrancheRecord>
        {
            new() { EntryPrice = 500m, Quantity = 10m, StopLoss = 450m, EnteredAt = DateTime.UtcNow },
            new() { EntryPrice = 550m, Quantity = 5m, StopLoss = 400m, EnteredAt = DateTime.UtcNow },
        };

        var result = service.ComputeCombinedStop(tranches, stopFloor: 450m);
        Assert.Equal(450m, result);
    }

    [Fact]
    public void ComputeCombinedStop_does_not_apply_floor_when_combined_is_higher()
    {
        var service = CreateService();
        // Tranche 1: stop = 450, Tranche 2: stop = 470 (tighter stop)
        // Combined average stop = (450*10 + 470*5)/15 = 456.67
        // Floor = highest individual stop = 470
        // Since combined (456.67) < floor (470), result should be floor (470)
        var tranches = new List<TrancheRecord>
        {
            new() { EntryPrice = 500m, Quantity = 10m, StopLoss = 450m, EnteredAt = DateTime.UtcNow },
            new() { EntryPrice = 550m, Quantity = 5m, StopLoss = 470m, EnteredAt = DateTime.UtcNow },
        };

        var result = service.ComputeCombinedStop(tranches, stopFloor: 470m);
        Assert.Equal(470m, result);
    }

    [Fact]
    public void ComputeCombinedStop_uses_weighted_average_when_no_floor()
    {
        var service = CreateService();
        var tranches = new List<TrancheRecord>
        {
            new() { EntryPrice = 500m, Quantity = 10m, StopLoss = 450m, EnteredAt = DateTime.UtcNow },
            new() { EntryPrice = 550m, Quantity = 5m, StopLoss = 400m, EnteredAt = DateTime.UtcNow },
        };

        // No floor: use weighted avg = 433.33
        var result = service.ComputeCombinedStop(tranches, stopFloor: null);
        Assert.Equal(433.33m, Math.Round(result, 2));
    }

    // ─── ComputeAddonQuantity (REQ-PYR-004) ────────────────────────────────────

    [Fact]
    public void ComputeAddonQuantity_first_add_is_base()
    {
        var service = CreateService();
        // 50% reduction, add level 1: base * (1 - 0.5)^0 = base
        var result = service.ComputeAddonQuantity(100m, 1, 50.0);
        Assert.Equal(100m, result);
    }

    [Fact]
    public void ComputeAddonQuantity_second_add_is_half()
    {
        var service = CreateService();
        // 50% reduction, add level 2: base * (1 - 0.5)^1 = base * 0.5
        var result = service.ComputeAddonQuantity(100m, 2, 50.0);
        Assert.Equal(50m, result);
    }

    [Fact]
    public void ComputeAddonQuantity_third_add_is_quarter()
    {
        var service = CreateService();
        // 50% reduction, add level 3: base * (1 - 0.5)^2 = base * 0.25
        var result = service.ComputeAddonQuantity(100m, 3, 50.0);
        Assert.Equal(25m, result);
    }

    [Fact]
    public void ComputeAddonQuantity_returns_zero_for_level_zero()
    {
        var service = CreateService();
        var result = service.ComputeAddonQuantity(100m, 0, 50.0);
        Assert.Equal(0m, result);
    }

    [Fact]
    public void ComputeAddonQuantity_returns_zero_for_zero_base()
    {
        var service = CreateService();
        var result = service.ComputeAddonQuantity(0m, 1, 50.0);
        Assert.Equal(0m, result);
    }

    // ─── Evaluate ─────────────────────────────────────────────────────────────

    [Fact]
    public void Evaluate_no_action_when_price_below_add_trigger()
    {
        // Arrange: avg entry = 500; add trigger = 5%; add price = 525
        // Current price = 510 < 525, so no add. R = (510-500)/(500-450) = 0.2, < 3, so no reduce.
        var service = CreateService();
        var input = new PyramidingEvaluationInput
        {
            CurrentPrice = 510m,
            AverageEntryPrice = 500m,
            Tranches = new List<TrancheRecord>
            {
                new() { EntryPrice = 500m, Quantity = 10m, StopLoss = 450m, EnteredAt = DateTime.UtcNow }
            },
            RemainingAddonEntries = 2,
            AddTriggerMovePct = 5.0,
            CurrentStopFloor = null,
            CurrentCombinedStop = null,
            BaseQuantity = 10m,
        };

        var result = service.Evaluate(input);

        Assert.False(result.ShouldAdd);
        Assert.False(result.ShouldReduce);
        Assert.Equal(500m, result.AverageEntryPrice);
        Assert.Equal(450m, result.CombinedStopPrice);
        Assert.Equal(10m, result.TotalQuantity);
    }

    [Fact]
    public void Evaluate_add_recommended_when_price_above_trigger()
    {
        // Arrange: avg entry = 500; add trigger = 5%; add price = 525
        // Current price = 530 >= 525, so add recommended.
        var service = CreateService();
        var input = new PyramidingEvaluationInput
        {
            CurrentPrice = 530m,
            AverageEntryPrice = 500m,
            Tranches = new List<TrancheRecord>
            {
                new() { EntryPrice = 500m, Quantity = 10m, StopLoss = 450m, EnteredAt = DateTime.UtcNow }
            },
            RemainingAddonEntries = 2,
            AddTriggerMovePct = 5.0,
            CurrentStopFloor = null,
            CurrentCombinedStop = null,
            BaseQuantity = 10m,
        };

        var result = service.Evaluate(input);

        Assert.True(result.ShouldAdd);
        Assert.Equal(1, result.AddLevel); // First add-on
        Assert.Equal(525m, result.AddPrice);
        Assert.Equal(500m, result.AverageEntryPrice);
    }

    [Fact]
    public void Evaluate_no_add_when_remaining_entries_zero()
    {
        // Arrange: price is above trigger, but no remaining add-on entries.
        var service = CreateService();
        var input = new PyramidingEvaluationInput
        {
            CurrentPrice = 530m,
            AverageEntryPrice = 500m,
            Tranches = new List<TrancheRecord>
            {
                new() { EntryPrice = 500m, Quantity = 10m, StopLoss = 450m, EnteredAt = DateTime.UtcNow },
                new() { EntryPrice = 525m, Quantity = 5m, StopLoss = 480m, EnteredAt = DateTime.UtcNow },
                new() { EntryPrice = 550m, Quantity = 3m, StopLoss = 500m, EnteredAt = DateTime.UtcNow },
            },
            RemainingAddonEntries = 0,
            AddTriggerMovePct = 5.0,
            CurrentStopFloor = null,
            CurrentCombinedStop = null,
            BaseQuantity = 10m,
        };

        var result = service.Evaluate(input);

        Assert.False(result.ShouldAdd);
    }

    [Fact]
    public void Evaluate_reduce_recommended_at_3r()
    {
        // Arrange: avg entry = 500; stop = 450; stopDistance = 50
        // Current price = 650 → profit = 150 → R = 150/50 = 3.0 → reduce recommended
        var service = CreateService();
        var input = new PyramidingEvaluationInput
        {
            CurrentPrice = 650m,
            AverageEntryPrice = 500m,
            Tranches = new List<TrancheRecord>
            {
                new() { EntryPrice = 500m, Quantity = 10m, StopLoss = 450m, EnteredAt = DateTime.UtcNow }
            },
            RemainingAddonEntries = 2,
            AddTriggerMovePct = 5.0,
            CurrentStopFloor = null,
            CurrentCombinedStop = null,
            BaseQuantity = 10m,
        };

        var result = service.Evaluate(input);

        Assert.True(result.ShouldReduce);
        Assert.Equal(650m, result.ReducePrice);
        Assert.Equal(3.0m, result.RMultiple);
    }

    [Fact]
    public void Evaluate_stop_floor_applied_flag()
    {
        // Arrange: two tranches, second has wider stop
        // Combined avg stop = (450*10 + 400*5)/15 = 433.33
        // Floor = highest individual = 450 (from first tranche)
        // Since combined < floor, floor is applied
        var service = CreateService();
        var input = new PyramidingEvaluationInput
        {
            CurrentPrice = 530m,
            AverageEntryPrice = 508m,
            Tranches = new List<TrancheRecord>
            {
                new() { EntryPrice = 500m, Quantity = 10m, StopLoss = 450m, EnteredAt = DateTime.UtcNow },
                new() { EntryPrice = 525m, Quantity = 5m, StopLoss = 400m, EnteredAt = DateTime.UtcNow },
            },
            RemainingAddonEntries = 1,
            AddTriggerMovePct = 5.0,
            CurrentStopFloor = 450m,
            CurrentCombinedStop = 433m,
            BaseQuantity = 10m,
        };

        var result = service.Evaluate(input);

        Assert.True(result.StopFloorApplied);
        Assert.Equal(450m, result.StopFloorValue);
        Assert.Equal(450m, result.CombinedStopPrice); // Floor value
    }

    // ─── Factory helpers ──────────────────────────────────────────────────────

    private static PyramidingAdvisoryService CreateService()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RmeModuleOptions
        {
            MaxPyramidingAddonEntries = 3,
            AddTriggerMovePct = 5.0,
            AddSizeReductionPct = 50.0,
        });
        return new PyramidingAdvisoryService(options, NullLogger<PyramidingAdvisoryService>.Instance);
    }
}
