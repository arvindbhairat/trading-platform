using SignalStack.Api.Backtesting;
using SignalStack.Signals.Backtesting;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// BTE-vs-RME parity check tests.
///
/// These tests exercise the shared <see cref="RmeConfigParser"/> against known
/// synthetic scenarios to verify that the backtest engine's RME-aware path
/// produces the same stop and sizing values as the live RME models would.
///
/// REQ-RME-003: identical inputs → identical outputs.
/// REQ-NFR-017: nightly parity check via GitHub Actions scheduled workflow.
/// </summary>
public sealed class BteRmeParityCheckTests
{
    private const decimal DefaultEquity = 10_000_000m;

    /// <summary>
    /// Fixed stop at 2%, fixed_percentage sizing at 0.5% risk.
    /// Entry 1500, stop = 1500 * (1 - 2/100) = 1470.
    /// Risk per share = 1500 - 1470 = 30.
    /// Risk capital = 10_000_000 * 0.5/100 = 50_000.
    /// Quantity = 50_000 / 30 = 1666.
    /// </summary>
    [Fact]
    public void FixedStop_2Pct_Sizing_0_5Pct_MatchesReference()
    {
        var config = BuildFixedConfig("fixed_percentage", 0.5m, "fixed", 2.0m);
        var (stopPrice, quantity) = RmeConfigParser.ComputePositionParameters(
            1500m, atr: null, config, DefaultEquity);

        Assert.Equal(1470m, stopPrice); // 1500 - 2%
        Assert.Equal(1666, quantity);   // 50_000 / 30
    }

    /// <summary>
    /// Fixed stop at 1%, fixed_percentage sizing at 1% risk.
    /// Entry 500, stop = 500 * (1 - 1/100) = 495.
    /// Risk per share = 500 - 495 = 5.
    /// Risk capital = 10_000_000 * 1/100 = 100_000.
    /// Quantity = 100_000 / 5 = 20_000.
    /// </summary>
    [Fact]
    public void FixedStop_1Pct_Risk1Pct_MatchesReference()
    {
        var config = BuildFixedConfig("fixed_percentage", 1.0m, "fixed", 1.0m);
        var (stopPrice, quantity) = RmeConfigParser.ComputePositionParameters(
            500m, atr: null, config, DefaultEquity);

        Assert.Equal(495m, stopPrice);     // 500 - 1%
        Assert.Equal(20_000, quantity);     // 100_000 / 5
    }

    /// <summary>
    /// ATR stop at 2x multiplier, fixed_percentage sizing at 0.5% risk.
    /// Entry 2500, ATR = 50, stop = 2500 - (50 * 2) = 2400.
    /// Risk per share = 2500 - 2400 = 100.
    /// Risk capital = 10_000_000 * 0.5/100 = 50_000.
    /// Quantity = 50_000 / 100 = 500.
    /// </summary>
    [Fact]
    public void AtrStop_2x_Sizing_0_5Pct_MatchesReference()
    {
        var config = BuildAtrConfig("fixed_percentage", 0.5m, 2.0m);
        var (stopPrice, quantity) = RmeConfigParser.ComputePositionParameters(
            2500m, atr: 50m, config, DefaultEquity);

        Assert.Equal(2400m, stopPrice); // 2500 - (50 * 2)
        Assert.Equal(500, quantity);    // 50_000 / 100
    }

    /// <summary>
    /// ATR stop at 3x multiplier, fixed_percentage sizing at 0.5% risk.
    /// Entry 800, ATR = 15, stop = 800 - (15 * 3) = 755.
    /// Risk per share = 800 - 755 = 45.
    /// Risk capital = 50_000.
    /// Quantity = 50_000 / 45 = 1111.
    /// </summary>
    [Fact]
    public void AtrStop_3x_Sizing_0_5Pct_MatchesReference()
    {
        var config = BuildAtrConfig("fixed_percentage", 0.5m, 3.0m);
        var (stopPrice, quantity) = RmeConfigParser.ComputePositionParameters(
            800m, atr: 15m, config, DefaultEquity);

        Assert.Equal(755m, stopPrice);  // 800 - (15 * 3)
        Assert.Equal(1111, quantity);   // 50_000 / 45
    }

    /// <summary>
    /// No RME config → legacy fallback: 5% stop, quantity 1.
    /// </summary>
    [Fact]
    public void NoRmeConfig_FallsBackToLegacyDefaults()
    {
        var (stopPrice, quantity) = RmeConfigParser.ComputePositionParameters(
            1000m, atr: null, rmeConfigJson: null, DefaultEquity);

        Assert.Equal(950m, stopPrice); // 1000 - 5%
        Assert.Equal(1, quantity);
    }

    /// <summary>
    /// Empty RME config → legacy fallback: 5% stop, quantity 1.
    /// </summary>
    [Fact]
    public void EmptyRmeConfig_FallsBackToLegacyDefaults()
    {
        var (stopPrice, quantity) = RmeConfigParser.ComputePositionParameters(
            1000m, atr: null, rmeConfigJson: "", DefaultEquity);

        Assert.Equal(950m, stopPrice);
        Assert.Equal(1, quantity);
    }

    /// <summary>
    /// Malformed RME config → fallback gracefully without throwing.
    /// </summary>
    [Fact]
    public void InvalidRmeConfig_FallsBackGracefully()
    {
        var (stopPrice, quantity) = RmeConfigParser.ComputePositionParameters(
            1000m, atr: null, rmeConfigJson: "{invalid}", DefaultEquity);

        Assert.Equal(950m, stopPrice);
        Assert.Equal(1, quantity);
    }

    /// <summary>
    /// Fixed stop with zero-distance risk (stop == entry) → minimum quantity 1.
    /// </summary>
    [Fact]
    public void ZeroRiskDistance_ReturnsMinimumQuantity()
    {
        var config = BuildFixedConfig("fixed_percentage", 0.5m, "fixed", 0.0m);
        var (stopPrice, quantity) = RmeConfigParser.ComputePositionParameters(
            1000m, atr: null, config, DefaultEquity);

        Assert.Equal(1000m, stopPrice); // 0% stop = no distance
        Assert.Equal(1, quantity);      // minimum
    }

    /// <summary>
    /// Small equity with fixed stop → quantity rounds down to minimum 1.
    /// </summary>
    [Fact]
    public void SmallEquity_MinimumQuantity()
    {
        var config = BuildFixedConfig("fixed_percentage", 0.5m, "fixed", 2.0m);
        var (stopPrice, quantity) = RmeConfigParser.ComputePositionParameters(
            1500m, atr: null, config, equity: 1000m);

        Assert.Equal(1470m, stopPrice);
        Assert.Equal(1, quantity); // risk capital = 1000 * 0.5% = 5, risk/share = 30, 5/30 = 0 → min 1
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    private static string BuildFixedConfig(
        string sizingType, decimal riskPct, string stopType, decimal stopPct)
    {
        return $$"""
            {
              "SizingModelType": "{{sizingType}}",
              "RiskPerTradePct": {{riskPct}},
              "StopLossType": "{{stopType}}",
              "StopLossPct": {{stopPct}},
              "Timeframe": "daily"
            }
            """;
    }

    private static string BuildAtrConfig(
        string sizingType, decimal riskPct, decimal atrMultiplier)
    {
        return $$"""
            {
              "SizingModelType": "{{sizingType}}",
              "RiskPerTradePct": {{riskPct}},
              "StopLossType": "atr",
              "AtrStopMultiplier": {{atrMultiplier}},
              "AtrPeriod": 14,
              "Timeframe": "daily"
            }
            """;
    }
}
