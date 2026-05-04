using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the ATR-based Trailing Stop Loss model (P6-T15).
/// Combines ATR-based distance calculation with trailing-stop semantics
/// (trailing from highest price since entry).
/// </summary>
public sealed class AtrTrailingStopLossTests
{
    // ─── Standard ATR trailing stop calculation ──────────────────────────────

    [Fact]
    public void Calculate_uses_atr_and_highest_price_for_stop()
    {
        // Arrange: entry = 1000; highest = 1200; ATR = 50; multiplier = 2.0
        // stop = 1200 - (50 * 2) = 1100
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: 50m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1200m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1100m, result.StopPrice);
        Assert.Equal("AtrTrailingStop", result.StopTypeUsed);
    }

    // ─── Falls back to entry price when no highest price provided ────────────

    [Fact]
    public void Calculate_falls_back_to_entry_price_when_highest_not_provided()
    {
        // Arrange: entry = 1000; no highest price; ATR = 50; multiplier = 2.0
        // stop = 1000 - (50 * 2) = 900
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: 50m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(900m, result.StopPrice);
        Assert.Equal("AtrTrailingStop", result.StopTypeUsed);
    }

    // ─── Custom ATR multiplier from options ──────────────────────────────────

    [Fact]
    public void Calculate_uses_custom_atr_multiplier_from_options()
    {
        // Arrange: entry = 1000; highest = 1500; ATR = 50; multiplier = 3.0
        // stop = 1500 - (50 * 3) = 1350
        var options = CreateOptions(atrMultiplier: 3.0);
        var model = CreateModel(options);
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: 50m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1500m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1350m, result.StopPrice);
    }

    // ─── Null ATR falls back to default fixed distance pct ───────────────────

    [Fact]
    public void Calculate_falls_back_to_default_distance_when_atr_is_null()
    {
        // Arrange: entry = 1000; highest = 1500; ATR = null; default distance = 5%
        // stop = 1500 * (1 - 0.05) = 1425
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: null,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1500m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1425m, result.StopPrice);
        Assert.Equal("AtrTrailingStop", result.StopTypeUsed);
    }

    // ─── Zero ATR falls back to default distance ────────────────────────────

    [Fact]
    public void Calculate_falls_back_to_default_distance_when_atr_is_zero()
    {
        // Arrange: entry = 1000; highest = 1200; ATR = 0; default distance = 5%
        // stop = 1200 * 0.95 = 1140
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: 0m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1200m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1140m, result.StopPrice);
    }

    // ─── Negative ATR falls back to default distance ─────────────────────────

    [Fact]
    public void Calculate_falls_back_to_default_distance_when_atr_is_negative()
    {
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: -10m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1200m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1140m, result.StopPrice); // 1200 * 0.95
    }

    // ─── Stop trails above entry when price has moved up ────────────────────

    [Fact]
    public void Calculate_stop_is_above_entry_when_price_has_moved_up()
    {
        // Arrange: entry = 500; highest = 1000; ATR = 100; multiplier = 2.0
        // stop = 1000 - 200 = 800 (above entry price of 500 — valid for trailing stop)
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 500m,
            AtrValue: 100m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1000m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.True(result.StopPrice > 500m,
            "Trailing stop should be above entry price when price has moved up significantly");
        Assert.Equal(800m, result.StopPrice);
    }

    // ─── Stop always below highest price ──────────────────────────────────

    [Fact]
    public void Calculate_stop_is_always_below_highest_price()
    {
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 100m,
            AtrValue: 200m, // wide ATR
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 200m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.True(result.StopPrice < 200m,
            "ATR trailing stop must be below the highest price since entry");
    }

    // ─── Clamp when stop would equal or exceed highest price ──────────────

    [Fact]
    public void Calculate_clamps_when_stop_would_equal_or_exceed_highest_price()
    {
        // Arrange: extremely narrow ATR with fallback that could produce
        // stop very close to highest. Use 0% default to force clamp scenario.
        var options = CreateOptions(defaultDistancePct: 0.0);
        var model = CreateModel(options);
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: 0m, // triggers fallback
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1000m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.True(result.StopPrice > 0m, "Stop price must be positive");
        Assert.True(result.StopPrice < 1000m, "Stop price must be below highest price");
    }

    // ─── Name property ─────────────────────────────────────────────────────

    [Fact]
    public void Name_returns_AtrTrailingStop()
    {
        var model = CreateModel();
        Assert.Equal("AtrTrailingStop", model.Name);
    }

    // ─── Rounding to two decimal places ─────────────────────────────────────

    [Fact]
    public void Calculate_rounds_stop_price_to_two_decimals()
    {
        // Arrange: entry = 1000; highest = 1234.567; ATR = 33.333; multiplier = 2.0
        // trailDistance = 33.333 * 2 = 66.666
        // stop = 1234.567 - 66.666 = 1167.901 → rounds to 1167.90
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: 33.333m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1234.567m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1167.90m, result.StopPrice);
    }

    // ─── Large price handling ──────────────────────────────────────────────

    [Fact]
    public void Calculate_handles_large_entry_and_highest_prices()
    {
        // Arrange: entry = 25000; highest = 35000; ATR = 500; multiplier = 2.0
        // stop = 35000 - (500 * 2) = 34000
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 25000m,
            AtrValue: 500m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 35000m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(34000m, result.StopPrice);
    }

    // ─── Zero/negative highest price falls back to entry price ──────────────

    [Fact]
    public void Calculate_falls_back_to_entry_when_highest_price_is_zero()
    {
        // Arrange: entry = 1000; highest = 0; ATR = 50
        // falls back to entry: stop = 1000 - (50 * 2) = 900
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: 50m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 0m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(900m, result.StopPrice);
    }

    [Fact]
    public void Calculate_falls_back_to_entry_when_highest_price_is_negative()
    {
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: 50m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = -500m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(900m, result.StopPrice);
    }

    // ─── Wide ATR → wider trail distance ─────────────────────────────────────

    [Fact]
    public void Calculate_returns_lower_stop_for_wider_atr()
    {
        // Arrange: entry = 1000; highest = 1000; ATR = 200; multiplier = 2.0
        // stop = 1000 - 400 = 600
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: 200m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1000m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(600m, result.StopPrice);
    }

    // ─── Narrow ATR → tighter stop ──────────────────────────────────────────

    [Fact]
    public void Calculate_returns_higher_stop_for_narrow_atr()
    {
        // Arrange: entry = 1000; highest = 1000; ATR = 10; multiplier = 2.0
        // stop = 1000 - 20 = 980
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: 10m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1000m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(980m, result.StopPrice);
    }

    // ─── Worst-case: missing ATR and highest price uses entry + default dist ──

    [Fact]
    public void Calculate_falls_back_to_entry_and_default_distance_when_both_missing()
    {
        // Arrange: entry = 1000; no highest, no ATR; default distance = 5%
        // stop = 1000 * 0.95 = 950
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
    }

    // ─── Wrong type in AdditionalInputs is ignored ─────────────────────────

    [Fact]
    public void Calculate_ignores_highest_price_with_wrong_type()
    {
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: 50m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = "not-a-decimal"
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(900m, result.StopPrice); // 1000 - (50 * 2)
    }

    // ─── Positive stop for all reasonable inputs ─────────────────────────────

    [Fact]
    public void Calculate_returns_positive_stop_for_all_valid_inputs()
    {
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 10m,
            AtrValue: 0.5m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 10m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.True(result.StopPrice > 0m, "Stop price must be positive");
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static AtrTrailingStopLoss CreateModel(RmeModuleOptions? options = null)
    {
        options ??= CreateOptions();
        return new AtrTrailingStopLoss(
            Options.Create(options),
            NullLogger<AtrTrailingStopLoss>.Instance);
    }

    private static RmeModuleOptions CreateOptions(
        double defaultDistancePct = 5.0,
        double atrMultiplier = 2.0)
    {
        return new RmeModuleOptions
        {
            DefaultRiskPerTradePct = 0.5,
            MinRiskPerTradePct = 0.5,
            MaxRiskPerTradePct = 1.0,
            MaxActivePositions = 200,
            DefaultFixedStopDistancePct = defaultDistancePct,
            AtrMultiplier = atrMultiplier,
        };
    }
}
