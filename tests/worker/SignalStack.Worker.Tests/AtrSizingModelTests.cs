using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the ATR-based sizing model (portfolio-risk-guidelines § Position Sizing Models).
/// </summary>
public sealed class AtrSizingModelTests
{
    // ─── Standard calculation ──────────────────────────────────────────────────

    [Fact]
    public void Calculate_uses_atr_for_volatility_adjusted_sizing()
    {
        // Arrange: equity = 1,00,000; risk = 0.5%; ATR = 50; multiplier = 2.0
        // riskAmount = 1,00,000 * 0.005 = 500
        // stopDistance = 50 * 2.0 = 100
        // quantity = floor(500 / 100) = 5
        var model = CreateModel();
        var input = CreateInput(entryPrice: 500m, atrValue: 50m, equity: 100_000m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(5m, result.RecommendedQuantity);
        Assert.Equal(500m, result.RiskAmount);
        Assert.Equal("AtrBased", result.ModelUsed);
        Assert.Null(result.AdvisoryMessage);
    }

    // ─── Missing ATR → zero quantity ──────────────────────────────────────────

    [Fact]
    public void Calculate_returns_zero_quantity_when_atr_is_missing()
    {
        var model = CreateModel();
        var input = new SizingInput(
            EntryPrice: 500m,
            StopPrice: 450m,
            AccountEquity: 100_000m,
            AvailableCapital: 100_000m,
            AdditionalInputs: null);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
        Assert.NotNull(result.AdvisoryMessage);
    }

    // ─── Zero ATR → zero quantity ─────────────────────────────────────────────

    [Fact]
    public void Calculate_returns_zero_quantity_when_atr_is_zero()
    {
        var model = CreateModel();
        var input = CreateInput(entryPrice: 500m, atrValue: 0m, equity: 100_000m);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
        Assert.NotNull(result.AdvisoryMessage);
    }

    // ─── Negative ATR → zero quantity ─────────────────────────────────────────

    [Fact]
    public void Calculate_returns_zero_quantity_when_atr_is_negative()
    {
        var model = CreateModel();
        var input = CreateInput(entryPrice: 500m, atrValue: -10m, equity: 100_000m);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
        Assert.NotNull(result.AdvisoryMessage);
    }

    // ─── Wide ATR → small quantity (high volatility) ──────────────────────────

    [Fact]
    public void Calculate_returns_smaller_quantity_for_wider_atr()
    {
        // Arrange: equity = 1,00,000; risk = 0.5%; ATR = 200; multiplier = 2.0
        // riskAmount = 500; stopDistance = 400; quantity = floor(500 / 400) = 1
        var model = CreateModel();
        var input = CreateInput(entryPrice: 1000m, atrValue: 200m, equity: 100_000m);

        var result = model.Calculate(input);

        Assert.Equal(1m, result.RecommendedQuantity);
    }

    // ─── Narrow ATR → larger quantity (low volatility) ────────────────────────

    [Fact]
    public void Calculate_returns_larger_quantity_for_narrow_atr()
    {
        // Arrange: equity = 1,00,000; risk = 0.5%; ATR = 10; multiplier = 2.0
        // riskAmount = 500; stopDistance = 20; quantity = floor(500 / 20) = 25
        var model = CreateModel();
        var input = CreateInput(entryPrice: 500m, atrValue: 10m, equity: 100_000m);

        var result = model.Calculate(input);

        Assert.Equal(25m, result.RecommendedQuantity);
    }

    // ─── Custom ATR multiplier ─────────────────────────────────────────────────

    [Fact]
    public void Calculate_uses_custom_atr_multiplier_from_options()
    {
        // Arrange: equity = 1,00,000; risk = 0.5%; ATR = 50; multiplier = 3.0
        // riskAmount = 500; stopDistance = 150; quantity = floor(500 / 150) = 3
        var options = CreateOptions(atrMultiplier: 3.0);
        var model = CreateModel(options);
        var input = CreateInput(entryPrice: 500m, atrValue: 50m, equity: 100_000m);

        var result = model.Calculate(input);

        Assert.Equal(3m, result.RecommendedQuantity);
    }

    // ─── Custom risk percentage ────────────────────────────────────────────────

    [Fact]
    public void Calculate_uses_custom_risk_pct_from_options()
    {
        // Arrange: equity = 1,00,000; risk = 1.0%; ATR = 50; multiplier = 2.0
        // riskAmount = 1,000; stopDistance = 100; quantity = floor(1000 / 100) = 10
        var options = CreateOptions(defaultRiskPct: 1.0);
        var model = CreateModel(options);
        var input = CreateInput(entryPrice: 500m, atrValue: 50m, equity: 100_000m);

        var result = model.Calculate(input);

        Assert.Equal(10m, result.RecommendedQuantity);
        Assert.Equal(1_000m, result.RiskAmount);
    }

    // ─── Large equity ─────────────────────────────────────────────────────────

    [Fact]
    public void Calculate_handles_large_equity_values()
    {
        // Arrange: equity = 10 Cr; risk = 0.5%; ATR = 100; multiplier = 2.0
        // riskAmount = 5,00,000; stopDistance = 200; quantity = floor(500000 / 200) = 2500
        var model = CreateModel();
        var input = CreateInput(entryPrice: 2500m, atrValue: 100m, equity: 10_00_00_000m);

        var result = model.Calculate(input);

        Assert.Equal(2500m, result.RecommendedQuantity);
        Assert.Equal(5_00_000m, result.RiskAmount);
    }

    // ─── Small equity → zero quantity ─────────────────────────────────────────

    [Fact]
    public void Calculate_returns_zero_quantity_when_equity_is_small()
    {
        // Arrange: equity = 10,000; risk = 0.5%; ATR = 50; multiplier = 2.0
        // riskAmount = 50; stopDistance = 100; quantity = floor(50 / 100) = 0
        var model = CreateModel();
        var input = CreateInput(entryPrice: 500m, atrValue: 50m, equity: 10_000m);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
        Assert.Equal(50m, result.RiskAmount);
    }

    // ─── Name property ────────────────────────────────────────────────────────

    [Fact]
    public void Name_returns_AtrBased()
    {
        var model = CreateModel();
        Assert.Equal("AtrBased", model.Name);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    private static AtrSizingModel CreateModel(RmeModuleOptions? options = null)
    {
        options ??= CreateOptions();
        return new AtrSizingModel(
            Options.Create(options),
            NullLogger<AtrSizingModel>.Instance);
    }

    private static RmeModuleOptions CreateOptions(
        double defaultRiskPct = 0.5,
        double atrMultiplier = 2.0)
    {
        return new RmeModuleOptions
        {
            DefaultRiskPerTradePct = defaultRiskPct,
            MinRiskPerTradePct = 0.5,
            MaxRiskPerTradePct = 1.0,
            MaxActivePositions = 200,
            AtrMultiplier = atrMultiplier,
        };
    }

    private static SizingInput CreateInput(decimal entryPrice, decimal atrValue, decimal equity)
    {
        return new SizingInput(
            EntryPrice: entryPrice,
            StopPrice: 0m,
            AccountEquity: equity,
            AvailableCapital: equity,
            AdditionalInputs: new Dictionary<string, object> { ["ATR"] = atrValue });
    }
}
