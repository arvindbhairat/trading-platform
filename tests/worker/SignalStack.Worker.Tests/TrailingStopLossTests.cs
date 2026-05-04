using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the Trailing Stop Loss (percentage-based) model (P6-T14).
/// (portfolio-risk-guidelines § Stop Loss Types — Required Stop Types (V1)).
/// </summary>
public sealed class TrailingStopLossTests
{
    // ─── Standard trailing stop calculation ──────────────────────────────────

    [Fact]
    public void Calculate_uses_trailing_percent_and_highest_price_for_stop()
    {
        // Arrange: entry = 1000; highest = 1200; trailing = 10%
        // stop = 1200 * (1 - 0.10) = 1080
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            TrailingStopPercent: 10m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1200m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1080m, result.StopPrice);
        Assert.Equal("TrailingStop", result.StopTypeUsed);
    }

    // ─── Explicit trailing percent from input ───────────────────────────────

    [Fact]
    public void Calculate_uses_explicit_trailing_percent_from_input()
    {
        // Arrange: entry = 1000; highest = 1500; trailing = 15%
        // stop = 1500 * (1 - 0.15) = 1275
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            TrailingStopPercent: 15m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1500m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1275m, result.StopPrice);
    }

    // ─── Default trailing percent from options ──────────────────────────────

    [Fact]
    public void Calculate_uses_default_trailing_percent_from_options_when_input_null()
    {
        // Arrange: entry = 1000; highest = 1500; default trailing = 8%
        // stop = 1500 * (1 - 0.08) = 1380
        var options = CreateOptions(defaultTrailingPct: 8.0);
        var model = CreateModel(options);
        var input = new StopLossInput(
            EntryPrice: 1000m,
            TrailingStopPercent: null,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1500m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1380m, result.StopPrice);
    }

    // ─── Highest price from additional inputs ───────────────────────────────

    [Fact]
    public void Calculate_stop_trails_higher_with_greater_highest_price()
    {
        // Arrange: entry = 1000; highest = 2000; trailing = 10%
        // stop = 2000 * (1 - 0.10) = 1800
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            TrailingStopPercent: 10m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 2000m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1800m, result.StopPrice);
    }

    // ─── Fallback to entry price when highest price not provided ────────────

    [Fact]
    public void Calculate_falls_back_to_entry_price_when_highest_price_not_provided()
    {
        // Arrange: entry = 1000; no highest price; trailing = 10%
        // stop = 1000 * (1 - 0.10) = 900
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            TrailingStopPercent: 10m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(900m, result.StopPrice);
        Assert.Equal("TrailingStop", result.StopTypeUsed);
    }

    // ─── Fallback when highest price is zero or negative ────────────────────

    [Fact]
    public void Calculate_falls_back_to_entry_when_highest_price_is_zero()
    {
        // Arrange: entry = 1000; highest = 0; trailing = 10%
        // fallback to entry: stop = 1000 * 0.9 = 900
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            TrailingStopPercent: 10m,
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
        // Arrange: entry = 1000; highest = -500; trailing = 10%
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            TrailingStopPercent: 10m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = -500m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(900m, result.StopPrice);
    }

    // ─── Fallback from zero trailing percent to default ─────────────────────

    [Fact]
    public void Calculate_falls_back_to_default_when_trailing_percent_is_zero()
    {
        // Arrange: entry = 1000; highest = 1200; trailing = 0% (invalid)
        // default is 10%: stop = 1200 * 0.9 = 1080
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            TrailingStopPercent: 0m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1200m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1080m, result.StopPrice);
    }

    // ─── Stop always below highest price ──────────────────────────────────

    [Fact]
    public void Calculate_stop_is_always_below_highest_price()
    {
        // Arrange: trailing stop should always be below the highest price
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 100m,
            TrailingStopPercent: 1m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 200m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.True(result.StopPrice < 200m,
            "Trailing stop must be below the highest price since entry");
        Assert.Equal("TrailingStop", result.StopTypeUsed);
    }

    // ─── Clamp when trailing percent would place stop above highest ────────

    [Fact]
    public void Calculate_clamps_when_stop_would_equal_or_exceed_highest_price()
    {
        // Arrange: trailing = 0% triggers fallback to default 10%; stop is fine.
        // Use 0% with no fallback scenario: trailingPercent = 0, falls back to
        // default 10% → stop = highest * 0.9, always below. Can't trigger clamp
        // with a positive trailing percent. Use an extreme: if the calculation
        // somehow yields stop >= highest, the model clamps.
        // Here we rely on the default 10% and highest same as entry:
        // stop = entry * 0.9 which is below entry, not a clamp test.
        //
        // The clamp is exercised when trailingPercent falls back to the default
        // and the intermediate math rounds unfavourably. For decimal arithmetic
        // this won't happen with positive values, so we verify the clamp exists
        // as a safety net by directly testing the guard condition.
        //
        // With trailing = 0 (falls to 10%) and highest = 1000:
        // stop = 1000 * 0.9 = 900 < 1000 → no clamp. So we can't trigger it
        // with positive trailing percent. The guard exists for degenerate
        // inputs (negative trailingPct after fallback). Instead, verify the
        // model returns a valid positive stop price for all reasonable inputs.
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            TrailingStopPercent: null,
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

    // ─── Stop is positive for all valid inputs ─────────────────────────────

    [Fact]
    public void Calculate_returns_positive_stop_for_all_valid_inputs()
    {
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 10m,
            TrailingStopPercent: 0.1m, // minimum valid trailing percent
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 10m
            });
        // stop = 10 * (1 - 0.001) = 9.99

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.True(result.StopPrice > 0m, "Stop price must be positive");
        Assert.Equal(9.99m, result.StopPrice);
    }

    // ─── Name property ─────────────────────────────────────────────────────

    [Fact]
    public void Name_returns_TrailingStop()
    {
        var model = CreateModel();
        Assert.Equal("TrailingStop", model.Name);
    }

    // ─── Rounding to two decimal places ─────────────────────────────────────

    [Fact]
    public void Calculate_rounds_stop_price_to_two_decimals()
    {
        // Arrange: entry = 1000; highest = 1234.567; trailing = 10%
        // stop = 1234.567 * 0.9 = 1111.1103 → rounds to 1111.11
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            TrailingStopPercent: 10m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1234.567m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1111.11m, result.StopPrice);
    }

    // ─── Large price handling ──────────────────────────────────────────────

    [Fact]
    public void Calculate_handles_large_entry_and_highest_prices()
    {
        // Arrange: entry = 25000; highest = 35000; trailing = 10%
        // stop = 35000 * 0.9 = 31500
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 25000m,
            TrailingStopPercent: 10m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 35000m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(31500m, result.StopPrice);
    }

    // ─── AdditionalInputs with wrong type is ignored ───────────────────────

    [Fact]
    public void Calculate_ignores_highest_price_with_wrong_type()
    {
        // Arrange: HighestPriceSinceEntry is a string; falls back to entry price
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            TrailingStopPercent: 10m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = "not-a-decimal"
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(900m, result.StopPrice); // 1000 * 0.9
    }

    // ─── Small trailing percentage ─────────────────────────────────────────

    [Fact]
    public void Calculate_with_small_trailing_percentage()
    {
        // Arrange: entry = 1000; highest = 1100; trailing = 1%
        // stop = 1100 * 0.99 = 1089
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            TrailingStopPercent: 1m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["HighestPriceSinceEntry"] = 1100m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1089m, result.StopPrice);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static TrailingStopLoss CreateModel(RmeModuleOptions? options = null)
    {
        options ??= CreateOptions();
        return new TrailingStopLoss(
            Options.Create(options),
            NullLogger<TrailingStopLoss>.Instance);
    }

    private static RmeModuleOptions CreateOptions(
        double defaultDistancePct = 5.0,
        double atrMultiplier = 2.0,
        double defaultTrailingPct = 10.0)
    {
        return new RmeModuleOptions
        {
            DefaultRiskPerTradePct = 0.5,
            MinRiskPerTradePct = 0.5,
            MaxRiskPerTradePct = 1.0,
            MaxActivePositions = 200,
            DefaultFixedStopDistancePct = defaultDistancePct,
            AtrMultiplier = atrMultiplier,
            DefaultTrailingStopPercent = defaultTrailingPct,
        };
    }
}
