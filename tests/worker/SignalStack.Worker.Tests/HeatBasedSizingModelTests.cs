using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the Portfolio Heat-based sizing model (portfolio-risk-guidelines § Position Sizing Models).
/// </summary>
public sealed class HeatBasedSizingModelTests
{
    // ─── Standard calculation ──────────────────────────────────────────────────

    [Fact]
    public void Calculate_sizes_position_for_remaining_heat_capacity()
    {
        // Arrange: equity = 1,00,000; current heat = 2%; max heat = 5%
        // currentOpenRisk = 1,00,000 * 0.02 = 2,000
        // maxAllowedOpenRisk = 1,00,000 * 0.05 = 5,000
        // available = 5,000 - 2,000 = 3,000
        // stopDistance = 500 - 450 = 50
        // quantity = floor(3000 / 50) = 60
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            currentHeat: 2m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(60m, result.RecommendedQuantity);
        Assert.Equal(3000m, result.RiskAmount);
        Assert.Equal("PortfolioHeat", result.ModelUsed);
        Assert.Null(result.AdvisoryMessage);
    }

    // ─── At max heat → zero quantity ──────────────────────────────────────────

    [Fact]
    public void Calculate_returns_zero_when_heat_at_maximum()
    {
        // Arrange: current heat = 5% equals max heat = 5%
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            currentHeat: 5m);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
        Assert.Equal(0m, result.RiskAmount);
        Assert.NotNull(result.AdvisoryMessage);
        Assert.Contains("at or above the maximum", result.AdvisoryMessage);
    }

    // ─── Above max heat → zero quantity ───────────────────────────────────────

    [Fact]
    public void Calculate_returns_zero_when_heat_exceeds_maximum()
    {
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            currentHeat: 6.5m);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
        Assert.Equal(0m, result.RiskAmount);
        Assert.NotNull(result.AdvisoryMessage);
    }

    // ─── Zero current heat ────────────────────────────────────────────────────

    [Fact]
    public void Calculate_uses_full_capacity_when_heat_is_zero()
    {
        // Arrange: equity = 1,00,000; current heat = 0%; max heat = 5%
        // available = 5,000 - 0 = 5,000
        // stopDistance = 500 - 450 = 50
        // quantity = floor(5000 / 50) = 100
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            currentHeat: 0m);

        var result = model.Calculate(input);

        Assert.Equal(100m, result.RecommendedQuantity);
        Assert.Equal(5000m, result.RiskAmount);
    }

    // ─── Missing heat input → treated as 0 ────────────────────────────────────

    [Fact]
    public void Calculate_defaults_to_zero_heat_when_input_missing()
    {
        var model = CreateModel();
        var input = new SizingInput(
            EntryPrice: 500m,
            StopPrice: 450m,
            AccountEquity: 100_000m,
            AvailableCapital: 100_000m,
            AdditionalInputs: null);

        var result = model.Calculate(input);

        // Should use full capacity (0% current heat)
        Assert.Equal(100m, result.RecommendedQuantity);
    }

    // ─── No stop price → uses entry price as proxy ────────────────────────────

    [Fact]
    public void Calculate_uses_entry_price_as_proxy_when_stop_price_missing()
    {
        // Arrange: equity = 1,00,000; current heat = 2%; max heat = 5%
        // available = 3,000; stopDistance = entryPrice = 500 (proxy)
        // quantity = floor(3000 / 500) = 6
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 0m,
            equity: 100_000m,
            currentHeat: 2m);

        var result = model.Calculate(input);

        Assert.Equal(6m, result.RecommendedQuantity);
        Assert.NotNull(result.AdvisoryMessage);
        Assert.Contains("entry price as risk proxy", result.AdvisoryMessage);
    }

    // ─── Custom max heat from options ─────────────────────────────────────────

    [Fact]
    public void Calculate_uses_custom_max_heat_from_options()
    {
        // Arrange: equity = 1,00,000; current heat = 3%; custom max heat = 10%
        // available = 10,000 - 3,000 = 7,000
        // stopDistance = 500 - 450 = 50
        // quantity = floor(7000 / 50) = 140
        var options = CreateOptions(maxPortfolioHeatPct: 10.0);
        var model = CreateModel(options);
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            currentHeat: 3m);

        var result = model.Calculate(input);

        Assert.Equal(140m, result.RecommendedQuantity);
        Assert.Equal(7000m, result.RiskAmount);
    }

    // ─── Large equity ─────────────────────────────────────────────────────────

    [Fact]
    public void Calculate_handles_large_equity_values()
    {
        // Arrange: equity = 10 Cr (10,00,00,000); current heat = 1%; max heat = 5%
        // available = 50,00,000 - 10,00,000 = 40,00,000
        // stopDistance = 2500 - 2300 = 200
        // quantity = floor(40,00,000 / 200) = 20,000
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 2500m,
            stopPrice: 2300m,
            equity: 10_00_00_000m,
            currentHeat: 1m);

        var result = model.Calculate(input);

        Assert.Equal(20_000m, result.RecommendedQuantity);
        Assert.Equal(40_00_000m, result.RiskAmount);
    }

    // ─── Small remaining capacity → zero quantity ─────────────────────────────

    [Fact]
    public void Calculate_returns_zero_when_remaining_capacity_is_too_small()
    {
        // Arrange: equity = 1,00,000; current heat = 4.9%; max heat = 5%
        // available = 100; stopDistance = 500
        // quantity = floor(100 / 500) = 0
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 0m,
            equity: 100_000m,
            currentHeat: 4.9m);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
    }

    // ─── Negative heat → treated as 0 ─────────────────────────────────────────

    [Fact]
    public void Calculate_treats_negative_heat_as_zero()
    {
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            currentHeat: -1m);

        var result = model.Calculate(input);

        // Should use full capacity (treated as 0%)
        Assert.Equal(100m, result.RecommendedQuantity);
    }

    // ─── Name property ───────────────────────────────────────────────────────

    [Fact]
    public void Name_returns_PortfolioHeat()
    {
        var model = CreateModel();
        Assert.Equal("PortfolioHeat", model.Name);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static HeatBasedSizingModel CreateModel(RmeModuleOptions? options = null)
    {
        options ??= CreateOptions();
        return new HeatBasedSizingModel(
            Options.Create(options),
            NullLogger<HeatBasedSizingModel>.Instance);
    }

    private static RmeModuleOptions CreateOptions(
        double defaultRiskPct = 0.5,
        double maxPortfolioHeatPct = 5.0,
        double atrMultiplier = 2.0)
    {
        return new RmeModuleOptions
        {
            DefaultRiskPerTradePct = defaultRiskPct,
            MinRiskPerTradePct = 0.5,
            MaxRiskPerTradePct = 1.0,
            MaxActivePositions = 200,
            AtrMultiplier = atrMultiplier,
            MaxPortfolioHeatPct = maxPortfolioHeatPct,
        };
    }

    private static SizingInput CreateInput(
        decimal entryPrice,
        decimal stopPrice,
        decimal equity,
        decimal currentHeat)
    {
        return new SizingInput(
            EntryPrice: entryPrice,
            StopPrice: stopPrice,
            AccountEquity: equity,
            AvailableCapital: equity,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["PortfolioHeat"] = currentHeat
            });
    }
}
