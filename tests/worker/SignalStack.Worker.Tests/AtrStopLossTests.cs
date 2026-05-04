using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the ATR-based Stop Loss model (P6-T13).
/// (portfolio-risk-guidelines § Stop Loss Types — ATR-based Stop Loss).
/// </summary>
public sealed class AtrStopLossTests
{
    // ─── Standard ATR stop calculation ───────────────────────────────────────────

    [Fact]
    public void Calculate_uses_atr_value_and_multiplier_for_stop_price()
    {
        // Arrange: entry = 1000; ATR = 50; multiplier = 2.0
        // stop = 1000 - (50 * 2.0) = 900
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m, AtrValue: 50m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(900m, result.StopPrice);
        Assert.Equal("AtrStop", result.StopTypeUsed);
    }

    // ─── Explicit stop price from AdditionalInputs ───────────────────────────────

    [Fact]
    public void Calculate_uses_explicit_stop_price_from_additional_inputs()
    {
        // Arrange: entry = 1000; ATR = 50; explicit stop = 850 overrides ATR calc
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: 50m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["AtrStopPrice"] = 850m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(850m, result.StopPrice);
    }

    // ─── Custom ATR multiplier from options ─────────────────────────────────────

    [Fact]
    public void Calculate_uses_custom_atr_multiplier_from_options()
    {
        // Arrange: entry = 1000; ATR = 50; multiplier = 3.0
        // stop = 1000 - (50 * 3.0) = 850
        var options = CreateOptions(atrMultiplier: 3.0);
        var model = CreateModel(options);
        var input = new StopLossInput(EntryPrice: 1000m, AtrValue: 50m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(850m, result.StopPrice);
    }

    // ─── Missing ATR → fallback to default distance ─────────────────────────────

    [Fact]
    public void Calculate_falls_back_to_default_distance_when_atr_is_null()
    {
        // Arrange: entry = 1000; ATR = null; default distance = 5%
        // stop = 1000 * (1 - 0.05) = 950
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m, AtrValue: null);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
        Assert.Equal("AtrStop", result.StopTypeUsed);
    }

    // ─── Zero ATR → fallback to default distance ────────────────────────────────

    [Fact]
    public void Calculate_falls_back_to_default_distance_when_atr_is_zero()
    {
        // Arrange: entry = 1000; ATR = 0; default distance = 5%
        // stop = 950
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m, AtrValue: 0m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
    }

    // ─── Negative ATR → fallback to default distance ────────────────────────────

    [Fact]
    public void Calculate_falls_back_to_default_distance_when_atr_is_negative()
    {
        // Arrange: entry = 1000; ATR = -10; default distance = 5%
        // stop = 950
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m, AtrValue: -10m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
    }

    // ─── Wide ATR → wider stop distance ─────────────────────────────────────────

    [Fact]
    public void Calculate_returns_lower_stop_for_wider_atr()
    {
        // Arrange: entry = 1000; ATR = 200; multiplier = 2.0
        // stop = 1000 - (200 * 2) = 600
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m, AtrValue: 200m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(600m, result.StopPrice);
    }

    // ─── Narrow ATR → tighter stop ─────────────────────────────────────────────

    [Fact]
    public void Calculate_returns_higher_stop_for_narrow_atr()
    {
        // Arrange: entry = 1000; ATR = 10; multiplier = 2.0
        // stop = 1000 - (10 * 2) = 980
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m, AtrValue: 10m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(980m, result.StopPrice);
    }

    // ─── Stop price clamped when at or above entry ──────────────────────────────

    [Fact]
    public void Calculate_clamps_stop_when_explicit_stop_is_above_entry()
    {
        // Arrange: entry = 500; explicit stop = 550 (above entry)
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 500m,
            AtrValue: 50m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["AtrStopPrice"] = 550m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.True(result.StopPrice < 500m);
        Assert.Equal("AtrStop", result.StopTypeUsed);
    }

    [Fact]
    public void Calculate_clamps_stop_when_atr_stop_equals_entry()
    {
        // Arrange: entry = 100; ATR = 100; multiplier = 1.0 → stop = 0 (below entry, OK)
        // But if the calculation yields a value >= entry, clamp should trigger.
        // We force it by setting ATR = 0, fallback distance = 0% → stop = entry
        var options = CreateOptions(defaultDistancePct: 0.0);
        var model = CreateModel(options);
        var input = new StopLossInput(EntryPrice: 1000m, AtrValue: 0m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.True(result.StopPrice < 1000m, "Stop price must be clamped below entry");
    }

    // ─── Name property ─────────────────────────────────────────────────────────

    [Fact]
    public void Name_returns_AtrStop()
    {
        var model = CreateModel();
        Assert.Equal("AtrStop", model.Name);
    }

    // ─── Rounding to two decimal places ─────────────────────────────────────────

    [Fact]
    public void Calculate_rounds_stop_price_to_two_decimals()
    {
        // Arrange: entry = 333.33; ATR = 25.25; multiplier = 2.0
        // stopDistance = 25.25 * 2 = 50.50
        // stop = 333.33 - 50.50 = 282.83
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 333.33m, AtrValue: 25.25m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(282.83m, result.StopPrice);
    }

    // ─── Large entry price ──────────────────────────────────────────────────────

    [Fact]
    public void Calculate_handles_large_entry_price()
    {
        // Arrange: entry = 25000; ATR = 500; multiplier = 2.0
        // stop = 25000 - (500 * 2) = 24000
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 25000m, AtrValue: 500m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(24000m, result.StopPrice);
    }

    // ─── AdditionalInputs with wrong type is ignored ────────────────────────────

    [Fact]
    public void Calculate_ignores_additional_input_with_wrong_type()
    {
        // Arrange: AtrStopPrice is a string instead of decimal; falls back to ATR calc
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AtrValue: 50m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["AtrStopPrice"] = "not-a-decimal"
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(900m, result.StopPrice); // ATR calc: 1000 - (50 * 2)
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static AtrStopLoss CreateModel(RmeModuleOptions? options = null)
    {
        options ??= CreateOptions();
        return new AtrStopLoss(
            Options.Create(options),
            NullLogger<AtrStopLoss>.Instance);
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
