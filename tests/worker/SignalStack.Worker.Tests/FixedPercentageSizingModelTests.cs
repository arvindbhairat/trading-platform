using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the Fixed Percentage Risk Per Trade sizing model (REQ-SIZING-005).
/// </summary>
public sealed class FixedPercentageSizingModelTests
{
    // ─── Default 0.5% risk ──────────────────────────────────────────────────

    [Fact]
    public void Calculate_uses_default_risk_pct_when_no_custom_options()
    {
        // Arrange: equity = 1,00,000; entry = 500; default risk = 0.5%
        // riskAmount = 1,00,000 * 0.005 = 500
        // quantity = floor(500 / 500) = 1
        var model = CreateModel();
        var input = new SizingInput(
            EntryPrice: 500m,
            StopPrice: 450m,
            AccountEquity: 100_000m,
            AvailableCapital: 100_000m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1m, result.RecommendedQuantity);
        Assert.Equal(500m, result.RiskAmount);
        Assert.Equal("FixedPercentage", result.ModelUsed);
        Assert.Null(result.AdvisoryMessage);
    }

    // ─── Custom risk percentage ──────────────────────────────────────────────

    [Fact]
    public void Calculate_uses_custom_risk_pct_from_options()
    {
        // Arrange: equity = 1,00,000; entry = 1000; risk = 1.0%
        // riskAmount = 1,00,000 * 0.01 = 1,000
        // quantity = floor(1000 / 1000) = 1
        var options = CreateOptions(defaultRiskPct: 1.0);
        var model = CreateModel(options);
        var input = new SizingInput(
            EntryPrice: 1000m,
            StopPrice: 900m,
            AccountEquity: 100_000m,
            AvailableCapital: 100_000m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1m, result.RecommendedQuantity);
        Assert.Equal(1000m, result.RiskAmount);
    }

    // ─── Zero entry price → zero quantity ────────────────────────────────────

    [Fact]
    public void Calculate_returns_zero_quantity_when_entry_price_is_zero()
    {
        var model = CreateModel();
        var input = new SizingInput(
            EntryPrice: 0m,
            StopPrice: 0m,
            AccountEquity: 100_000m,
            AvailableCapital: 100_000m);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
        Assert.Equal(500m, result.RiskAmount); // risk amount still computed
    }

    // ─── Name property ───────────────────────────────────────────────────────

    [Fact]
    public void Name_returns_FixedPercentage()
    {
        var model = CreateModel();
        Assert.Equal("FixedPercentage", model.Name);
    }

    // ─── Large equity ────────────────────────────────────────────────────────

    [Fact]
    public void Calculate_handles_large_equity_values()
    {
        // Arrange: equity = 10 Cr (10,00,00,000); entry = 2500; risk = 0.5%
        // riskAmount = 10,00,00,000 * 0.005 = 5,00,000
        // quantity = floor(5,00,000 / 2500) = 200
        var model = CreateModel();
        var input = new SizingInput(
            EntryPrice: 2500m,
            StopPrice: 2300m,
            AccountEquity: 10_00_00_000m,
            AvailableCapital: 10_00_00_000m);

        var result = model.Calculate(input);

        Assert.Equal(200m, result.RecommendedQuantity);
        Assert.Equal(5_00_000m, result.RiskAmount);
    }

    // ─── Small equity → zero quantity ────────────────────────────────────────

    [Fact]
    public void Calculate_returns_zero_quantity_when_equity_is_small()
    {
        // Arrange: equity = 10,000; entry = 500; risk = 0.5%
        // riskAmount = 10,000 * 0.005 = 50
        // quantity = floor(50 / 500) = 0
        var model = CreateModel();
        var input = new SizingInput(
            EntryPrice: 500m,
            StopPrice: 450m,
            AccountEquity: 10_000m,
            AvailableCapital: 10_000m);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
        Assert.Equal(50m, result.RiskAmount);
    }

    // ─── Fractional quantity floored ─────────────────────────────────────────

    [Fact]
    public void Calculate_floors_fractional_quantities()
    {
        // Arrange: equity = 1,00,000; entry = 750; risk = 0.5%
        // riskAmount = 1,00,000 * 0.005 = 500
        // quantity = floor(500 / 750) = floor(0.666) = 0
        var model = CreateModel();
        var input = new SizingInput(
            EntryPrice: 750m,
            StopPrice: 680m,
            AccountEquity: 100_000m,
            AvailableCapital: 100_000m);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
    }

    // ─── Minimum risk (0.1%) ─────────────────────────────────────────────────

    [Fact]
    public void Calculate_supports_minimum_risk_pct()
    {
        // Arrange: equity = 10,00,000; entry = 100; risk = 0.1%
        // riskAmount = 10,00,000 * 0.001 = 1,000
        // quantity = floor(1000 / 100) = 10
        var options = CreateOptions(defaultRiskPct: 0.1);
        var model = CreateModel(options);
        var input = new SizingInput(
            EntryPrice: 100m,
            StopPrice: 90m,
            AccountEquity: 10_00_000m,
            AvailableCapital: 10_00_000m);

        var result = model.Calculate(input);

        Assert.Equal(10m, result.RecommendedQuantity);
        Assert.Equal(1_000m, result.RiskAmount);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static FixedPercentageSizingModel CreateModel(RmeModuleOptions? options = null)
    {
        options ??= CreateOptions();
        return new FixedPercentageSizingModel(
            Microsoft.Extensions.Options.Options.Create(options),
            NullLogger<FixedPercentageSizingModel>.Instance);
    }

    private static RmeModuleOptions CreateOptions(double defaultRiskPct = 0.5)
    {
        return new RmeModuleOptions
        {
            DefaultRiskPerTradePct = defaultRiskPct,
            MinRiskPerTradePct = 0.5,
            MaxRiskPerTradePct = 1.0,
            MaxActivePositions = 200,
        };
    }
}
