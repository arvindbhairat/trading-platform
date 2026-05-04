using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the Drawdown-adjusted sizing model (portfolio-risk-guidelines § Position Sizing Models).
/// </summary>
public sealed class DrawdownAdjustedSizingModelTests
{
    // ─── No drawdown → full size ──────────────────────────────────────────────

    [Fact]
    public void Calculate_uses_full_size_when_no_drawdown()
    {
        // Arrange: equity = 1,00,000; risk = 0.5%; drawdown = 0%
        // baseRisk = 500; factor = 1.0; stopDistance = 50; qty = floor(500/50) = 10
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            drawdownPct: 0m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(10m, result.RecommendedQuantity);
        Assert.Equal(500m, result.RiskAmount);
        Assert.Equal("DrawdownAdjusted", result.ModelUsed);
        Assert.Null(result.AdvisoryMessage);
    }

    // ─── Drawdown below reduction start → full size ─────────────────────────

    [Fact]
    public void Calculate_uses_full_size_when_drawdown_below_threshold()
    {
        // Arrange: drawdown = 3%; reductionStart = 5%; blockPct = 20%
        // factor = 1.0; qty = floor(500/50) = 10
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            drawdownPct: 3m);

        var result = model.Calculate(input);

        Assert.Equal(10m, result.RecommendedQuantity);
        Assert.Equal(500m, result.RiskAmount);
        Assert.Null(result.AdvisoryMessage);
    }

    // ─── Drawdown at reduction start → full size ────────────────────────────

    [Fact]
    public void Calculate_uses_full_size_when_drawdown_at_reduction_start()
    {
        // Arrange: drawdown = 5%; reductionStart = 5%; blockPct = 20%
        // factor = 1.0 (<= threshold); qty = 10
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            drawdownPct: 5m);

        var result = model.Calculate(input);

        Assert.Equal(10m, result.RecommendedQuantity);
        Assert.Equal(500m, result.RiskAmount);
        Assert.Null(result.AdvisoryMessage);
    }

    // ─── Drawdown above reduction start → reduced size ──────────────────────

    [Fact]
    public void Calculate_reduces_size_when_drawdown_above_threshold()
    {
        // Arrange: drawdown = 10%; reductionStart = 5%; blockPct = 20%
        // factor = 1.0 − (10−5)/(20−5) = 1.0 − 5/15 = 0.666...
        // adjustedRisk = 500 * 0.666... = 333.33...
        // stopDistance = 50; qty = floor(333.33.../50) = 6
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            drawdownPct: 10m);

        var result = model.Calculate(input);

        Assert.Equal(6m, result.RecommendedQuantity);
        // RiskAmount carries the adjusted risk with floating-point imprecision; verify it is ≈ 333.33
        Assert.True(result.RiskAmount > 333m && result.RiskAmount < 334m,
            $"Expected RiskAmount near 333.33, got {result.RiskAmount}");
        Assert.NotNull(result.AdvisoryMessage);
        Assert.Contains("reduced by factor", result.AdvisoryMessage);
    }

    // ─── Drawdown at midpoint → 50% reduction ───────────────────────────────

    [Fact]
    public void Calculate_applies_half_size_at_midpoint_drawdown()
    {
        // Arrange: drawdown = 12.5%; reductionStart = 5%; blockPct = 20%
        // factor = 1.0 − (12.5−5)/(20−5) = 1.0 − 7.5/15 = 0.5
        // adjustedRisk = 500 * 0.5 = 250
        // stopDistance = 50; qty = floor(250/50) = 5
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            drawdownPct: 12.5m);

        var result = model.Calculate(input);

        Assert.Equal(5m, result.RecommendedQuantity);
        Assert.Equal(250m, result.RiskAmount);
        Assert.NotNull(result.AdvisoryMessage);
    }

    // ─── Drawdown at block threshold → zero quantity ────────────────────────

    [Fact]
    public void Calculate_returns_zero_when_drawdown_at_block_threshold()
    {
        // Arrange: drawdown = 20%; reductionStart = 5%; blockPct = 20%
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            drawdownPct: 20m);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
        Assert.Equal(0m, result.RiskAmount);
        Assert.NotNull(result.AdvisoryMessage);
        Assert.Contains("at or above the block", result.AdvisoryMessage);
    }

    // ─── Drawdown above block threshold → zero quantity ─────────────────────

    [Fact]
    public void Calculate_returns_zero_when_drawdown_exceeds_block_threshold()
    {
        // Arrange: drawdown = 25%; reductionStart = 5%; blockPct = 20%
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            drawdownPct: 25m);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
        Assert.Equal(0m, result.RiskAmount);
        Assert.NotNull(result.AdvisoryMessage);
    }

    // ─── Missing drawdown → treated as 0 (no reduction) ─────────────────────

    [Fact]
    public void Calculate_defaults_to_zero_drawdown_when_input_missing()
    {
        // Arrange: drawdown not provided → treated as 0% → no reduction
        // qty = floor(500/50) = 10
        var model = CreateModel();
        var input = new SizingInput(
            EntryPrice: 500m,
            StopPrice: 450m,
            AccountEquity: 100_000m,
            AvailableCapital: 100_000m,
            AdditionalInputs: null);

        var result = model.Calculate(input);

        Assert.Equal(10m, result.RecommendedQuantity);
        Assert.Equal(500m, result.RiskAmount);
        Assert.Null(result.AdvisoryMessage);
    }

    // ─── Custom reduction start pct ─────────────────────────────────────────

    [Fact]
    public void Calculate_uses_custom_reduction_start_pct()
    {
        // Arrange: drawdown = 8%; custom reductionStart = 10%; blockPct = 20%
        // drawdown(8%) < reductionStart(10%) → factor = 1.0 → no reduction
        var options = CreateOptions(drawdownReductionStartPct: 10.0);
        var model = CreateModel(options);
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            drawdownPct: 8m);

        var result = model.Calculate(input);

        Assert.Equal(10m, result.RecommendedQuantity);
        Assert.Null(result.AdvisoryMessage);
    }

    // ─── Custom block pct ──────────────────────────────────────────────────

    [Fact]
    public void Calculate_uses_custom_block_pct()
    {
        // Arrange: custom blockPct = 15%; drawdown = 15%
        // drawdown >= blockPct → zero
        var options = CreateOptions(drawdownBlockPct: 15.0);
        var model = CreateModel(options);
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            drawdownPct: 15m);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
        Assert.Equal(0m, result.RiskAmount);
        Assert.NotNull(result.AdvisoryMessage);
    }

    // ─── Custom risk percentage ─────────────────────────────────────────────

    [Fact]
    public void Calculate_uses_custom_risk_pct_from_options()
    {
        // Arrange: drawdown = 0%; risk = 1.0%; equity = 1,00,000
        // baseRisk = 1,000; stopDistance = 50; qty = floor(1000/50) = 20
        var options = CreateOptions(defaultRiskPct: 1.0);
        var model = CreateModel(options);
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            drawdownPct: 0m);

        var result = model.Calculate(input);

        Assert.Equal(20m, result.RecommendedQuantity);
        Assert.Equal(1_000m, result.RiskAmount);
    }

    // ─── Large equity ──────────────────────────────────────────────────────

    [Fact]
    public void Calculate_handles_large_equity_values()
    {
        // Arrange: equity = 10 Cr (10,00,00,000); risk = 0.5%; drawdown = 0%
        // baseRisk = 5,00,000; stopDistance = 200; qty = floor(500000/200) = 2500
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 2500m,
            stopPrice: 2300m,
            equity: 10_00_00_000m,
            drawdownPct: 0m);

        var result = model.Calculate(input);

        Assert.Equal(2500m, result.RecommendedQuantity);
        Assert.Equal(5_00_000m, result.RiskAmount);
    }

    // ─── Small equity → zero quantity ──────────────────────────────────────

    [Fact]
    public void Calculate_returns_zero_quantity_when_equity_is_small()
    {
        // Arrange: equity = 10,000; risk = 0.5%; drawdown = 0%
        // baseRisk = 50; stopDistance = 50; qty = floor(50/50) = 1
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 10_000m,
            drawdownPct: 0m);

        var result = model.Calculate(input);

        Assert.Equal(1m, result.RecommendedQuantity);
        Assert.Equal(50m, result.RiskAmount);
    }

    // ─── Very small equity → zero quantity after floor ──────────────────────

    [Fact]
    public void Calculate_returns_zero_quantity_when_adjusted_risk_is_very_small()
    {
        // Arrange: equity = 5,000; risk = 0.5%; drawdown = 10%
        // baseRisk = 25; factor = 0.666...; adjustedRisk = 16.66...
        // stopDistance = 50; qty = floor(16.66.../50) = 0
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 5_000m,
            drawdownPct: 10m);

        var result = model.Calculate(input);

        Assert.Equal(0m, result.RecommendedQuantity);
    }

    // ─── Zero stop distance → uses entry price as proxy ──────────────────

    [Fact]
    public void Calculate_uses_entry_price_as_proxy_when_stop_distance_invalid()
    {
        // Arrange: stopPrice > entryPrice → fall through to entryPrice proxy
        // equity = 1,00,000; risk = 0.5%; drawdown = 0%
        // baseRisk = 500; stopDistance = 500 (entryPrice proxy); qty = floor(500/500) = 1
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 550m,
            equity: 100_000m,
            drawdownPct: 0m);

        var result = model.Calculate(input);

        Assert.Equal(1m, result.RecommendedQuantity);
        Assert.Equal(500m, result.RiskAmount);
    }

    // ─── Negative drawdown → treated as 0 ──────────────────────────────────

    [Fact]
    public void Calculate_treats_negative_drawdown_as_zero()
    {
        // Arrange: drawdown = -2% → treated as 0% → no reduction
        // qty = floor(500/50) = 10
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 450m,
            equity: 100_000m,
            drawdownPct: -2m);

        var result = model.Calculate(input);

        Assert.Equal(10m, result.RecommendedQuantity);
        Assert.Null(result.AdvisoryMessage);
    }

    // ─── Drawdown with no stop price → uses entry price as proxy ────────────

    [Fact]
    public void Calculate_uses_entry_price_as_proxy_when_stop_price_missing()
    {
        // Arrange: drawdown = 0%; equity = 1,00,000; stopPrice = 0
        // baseRisk = 500; stopDistance = entryPrice = 500; qty = floor(500/500) = 1
        var model = CreateModel();
        var input = CreateInput(
            entryPrice: 500m,
            stopPrice: 0m,
            equity: 100_000m,
            drawdownPct: 0m);

        var result = model.Calculate(input);

        Assert.Equal(1m, result.RecommendedQuantity);
        Assert.Equal(500m, result.RiskAmount);
    }

    // ─── Name property ─────────────────────────────────────────────────────

    [Fact]
    public void Name_returns_DrawdownAdjusted()
    {
        var model = CreateModel();
        Assert.Equal("DrawdownAdjusted", model.Name);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────

    private static DrawdownAdjustedSizingModel CreateModel(RmeModuleOptions? options = null)
    {
        options ??= CreateOptions();
        return new DrawdownAdjustedSizingModel(
            Options.Create(options),
            NullLogger<DrawdownAdjustedSizingModel>.Instance);
    }

    private static RmeModuleOptions CreateOptions(
        double defaultRiskPct = 0.5,
        double drawdownReductionStartPct = 5.0,
        double drawdownBlockPct = 20.0)
    {
        return new RmeModuleOptions
        {
            DefaultRiskPerTradePct = defaultRiskPct,
            MinRiskPerTradePct = 0.5,
            MaxRiskPerTradePct = 1.0,
            MaxActivePositions = 200,
            DrawdownReductionStartPct = drawdownReductionStartPct,
            DrawdownBlockPct = drawdownBlockPct,
        };
    }

    private static SizingInput CreateInput(
        decimal entryPrice,
        decimal stopPrice,
        decimal equity,
        decimal drawdownPct)
    {
        return new SizingInput(
            EntryPrice: entryPrice,
            StopPrice: stopPrice,
            AccountEquity: equity,
            AvailableCapital: equity,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentDrawdownPct"] = drawdownPct
            });
    }
}
