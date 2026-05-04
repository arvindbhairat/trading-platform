using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the Fixed Stop Loss model (P6-T12).
/// (portfolio-risk-guidelines § Stop Loss Types — Fixed Stop Loss).
/// </summary>
public sealed class FixedStopLossTests
{
    // ─── Default distance (5%) ────────────────────────────────────────────────

    [Fact]
    public void Calculate_uses_default_distance_when_no_explicit_stop_price()
    {
        // Arrange: entry = 1000; default distance = 5%
        // stop = 1000 * (1 - 0.05) = 950
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
        Assert.Equal("FixedStop", result.StopTypeUsed);
    }

    // ─── Explicit stop price from AdditionalInputs ────────────────────────────

    [Fact]
    public void Calculate_uses_explicit_stop_price_from_additional_inputs()
    {
        // Arrange: entry = 1000; explicit stop = 920
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["FixedStopPrice"] = 920m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(920m, result.StopPrice);
    }

    // ─── Custom default distance from options ─────────────────────────────────

    [Fact]
    public void Calculate_uses_custom_default_distance_from_options()
    {
        // Arrange: entry = 2000; default distance = 3%
        // stop = 2000 * (1 - 0.03) = 1940
        var options = CreateOptions(defaultDistancePct: 3.0);
        var model = CreateModel(options);
        var input = new StopLossInput(EntryPrice: 2000m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1940m, result.StopPrice);
    }

    // ─── Stop price clamped when at or above entry ────────────────────────────

    [Fact]
    public void Calculate_clamps_stop_when_explicit_stop_is_above_entry()
    {
        // Arrange: entry = 500; explicit stop = 550 (above entry)
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 500m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["FixedStopPrice"] = 550m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.True(result.StopPrice < 500m);
        Assert.Equal("FixedStop", result.StopTypeUsed);
    }

    [Fact]
    public void Calculate_clamps_stop_when_default_stop_equals_entry()
    {
        // Arrange: entry = 1000; distance = 0% → stop = 1000 (equals entry)
        var options = CreateOptions(defaultDistancePct: 0.0);
        var model = CreateModel(options);
        var input = new StopLossInput(EntryPrice: 1000m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.True(result.StopPrice < 1000m, "Stop price must be clamped below entry");
    }

    // ─── Name property ────────────────────────────────────────────────────────

    [Fact]
    public void Name_returns_FixedStop()
    {
        var model = CreateModel();
        Assert.Equal("FixedStop", model.Name);
    }

    // ─── Rounding to two decimal places ───────────────────────────────────────

    [Fact]
    public void Calculate_rounds_stop_price_to_two_decimals()
    {
        // Arrange: entry = 333.33; default distance = 5%
        // stop = 333.33 * 0.95 = 316.6635 → rounds to 316.66
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 333.33m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(316.66m, result.StopPrice);
    }

    // ─── Large entry price ────────────────────────────────────────────────────

    [Fact]
    public void Calculate_handles_large_entry_price()
    {
        // Arrange: entry = 25000; default distance = 5%
        // stop = 25000 * 0.95 = 23750
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 25000m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(23750m, result.StopPrice);
    }

    // ─── AdditionalInputs with wrong type is ignored ──────────────────────────

    [Fact]
    public void Calculate_ignores_additional_input_with_wrong_type()
    {
        // Arrange: FixedStopPrice is a string instead of decimal; fallback to default
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["FixedStopPrice"] = "not-a-decimal"
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice); // default 5% below 1000
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static FixedStopLoss CreateModel(RmeModuleOptions? options = null)
    {
        options ??= CreateOptions();
        return new FixedStopLoss(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<FixedStopLoss>.Instance);
    }

    private static RmeModuleOptions CreateOptions(double defaultDistancePct = 5.0)
    {
        return new RmeModuleOptions
        {
            DefaultRiskPerTradePct = 0.5,
            MinRiskPerTradePct = 0.5,
            MaxRiskPerTradePct = 1.0,
            MaxActivePositions = 200,
            DefaultFixedStopDistancePct = defaultDistancePct,
        };
    }
}
