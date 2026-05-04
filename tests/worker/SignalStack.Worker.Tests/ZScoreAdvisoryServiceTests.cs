using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the Z-Score Advisory Overlay service: z-score computation,
/// threshold breach detection (REQ-SIZING-007), and price level mapping
/// (REQ-SIZING-008).
/// </summary>
public sealed class ZScoreAdvisoryServiceTests
{
    // ─── Edge cases ───────────────────────────────────────────────────────────

    [Fact]
    public void ComputeAdvisory_returns_neutral_when_insufficient_data()
    {
        var service = CreateService();
        var result = service.ComputeAdvisory([]);

        Assert.Equal(0d, result.ZScore);
        Assert.Equal(0, result.SampleCount);
        Assert.NotNull(result.AdvisoryMessage);
        Assert.False(result.AddAdvisory);
        Assert.False(result.ReduceAdvisory);
    }

    [Fact]
    public void ComputeAdvisory_returns_neutral_for_single_price()
    {
        var service = CreateService();
        var result = service.ComputeAdvisory([500m]);

        Assert.Equal(0d, result.ZScore);
        Assert.Equal(0, result.SampleCount);
        Assert.NotNull(result.AdvisoryMessage);
    }

    [Fact]
    public void ComputeAdvisory_returns_neutral_when_stddev_is_zero()
    {
        var service = CreateService();
        // All identical prices → stddev = 0
        var result = service.ComputeAdvisory([500m, 500m, 500m]);

        Assert.Equal(0d, result.ZScore);
        Assert.Equal(3, result.SampleCount);
        Assert.NotNull(result.AdvisoryMessage);
        Assert.False(result.AddAdvisory);
        Assert.False(result.ReduceAdvisory);
    }

    // ─── Normal range ─────────────────────────────────────────────────────────

    [Fact]
    public void ComputeAdvisory_no_advisory_when_zscore_in_normal_range()
    {
        var service = CreateService();
        // Closing prices with moderate variance, latest price near mean
        var prices = new List<decimal> { 500m, 505m, 495m, 502m, 498m, 500m };
        var result = service.ComputeAdvisory(prices);

        Assert.False(result.AddAdvisory);
        Assert.False(result.ReduceAdvisory);
        Assert.Equal("rolling_5d", result.Timeframe);
        Assert.Equal(6, result.SampleCount);
        Assert.InRange(result.ZScore, -3d, 3d);
    }

    // ─── Add advisory ─────────────────────────────────────────────────────────

    [Fact]
    public void ComputeAdvisory_add_advisory_when_zscore_exceeds_add_threshold()
    {
        var service = CreateService();
        // With N identical values followed by a different value, z = sqrt(N).
        // Using 10 identical + 1 extreme gives z = sqrt(10) ≈ 3.16 > 3.
        var prices = new List<decimal>
        {
            100m, 100m, 100m, 100m, 100m,
            100m, 100m, 100m, 100m, 100m,
            200m
        };
        var result = service.ComputeAdvisory(prices);

        Assert.True(result.AddAdvisory, $"Z-score was {result.ZScore:F4}, expected >= 3");
        Assert.Equal(3.0d, result.AddThreshold);
        Assert.NotNull(result.AdvisoryMessage);
        Assert.NotNull(result.AddPriceLevel);
        Assert.True(result.AddPriceLevel > 0m);
    }

    // ─── Reduce advisory ──────────────────────────────────────────────────────

    [Fact]
    public void ComputeAdvisory_reduce_advisory_when_zscore_below_reduce_threshold()
    {
        var service = CreateService();
        // With N identical values followed by a different value, z = sqrt(N).
        // Using 10 identical (200) + 1 low (100) gives z ≈ -3.16 < -3.
        var prices = new List<decimal>
        {
            200m, 200m, 200m, 200m, 200m,
            200m, 200m, 200m, 200m, 200m,
            100m
        };
        var result = service.ComputeAdvisory(prices);

        Assert.True(result.ReduceAdvisory, $"Z-score was {result.ZScore:F4}, expected <= -3");
        Assert.Equal(-3.0d, result.ReduceThreshold);
        Assert.NotNull(result.AdvisoryMessage);
        Assert.NotNull(result.ReducePriceLevel);
        Assert.True(result.ReducePriceLevel > 0m);
    }

    // ─── Price level mapping (REQ-SIZING-008) ─────────────────────────────────

    [Fact]
    public void ComputeAdvisory_maps_thresholds_to_price_levels()
    {
        var service = CreateService();
        // Stable prices so we get predictable mean and stddev
        var prices = new List<decimal> { 100m, 102m, 98m, 101m, 99m };
        var result = service.ComputeAdvisory(prices);

        // Mean = (100+102+98+101+99)/5 = 100
        // Variance = ((0+4+4+1+1)/5) = 2, StdDev = sqrt(2) ≈ 1.414
        // AddPriceLevel = 100 + 3*1.414 ≈ 104.24
        // ReducePriceLevel = 100 + (-3)*1.414 ≈ 95.76

        Assert.NotNull(result.AddPriceLevel);
        Assert.NotNull(result.ReducePriceLevel);
        Assert.True(result.AddPriceLevel > result.ReducePriceLevel,
            "Add price level should be above reduce price level");
    }

    // ─── Custom timeframe ──────────────────────────────────────────────────────

    [Fact]
    public void ComputeAdvisory_uses_custom_timeframe()
    {
        var service = CreateService();
        var prices = new List<decimal> { 500m, 502m, 498m, 501m, 499m, 503m };
        var result = service.ComputeAdvisory(prices, timeframe: "rolling_3d");

        Assert.Equal("rolling_3d", result.Timeframe);
    }

    // ─── Statistical correctness ──────────────────────────────────────────────

    [Fact]
    public void ComputeAdvisory_zscore_is_zero_at_mean()
    {
        var service = CreateService();
        // Latest price equals the mean
        var prices = new List<decimal> { 100m, 102m, 98m, 100m };
        var result = service.ComputeAdvisory(prices);

        // Mean = 100, latest = 100, so z-score ≈ 0
        Assert.InRange(Math.Abs(result.ZScore), 0d, 0.5d);
    }

    [Fact]
    public void ComputeAdvisory_provides_mean_and_stddev()
    {
        var service = CreateService();
        var prices = new List<decimal> { 100m, 110m, 90m };
        var result = service.ComputeAdvisory(prices);

        Assert.Equal(100d, result.Mean, 1); // Mean ≈ 100
        Assert.True(result.StandardDeviation > 0d);
    }

    // ─── Factory helpers ──────────────────────────────────────────────────────

    private static ZScoreAdvisoryService CreateService()
    {
        var options = Microsoft.Extensions.Options.Options.Create(new RmeModuleOptions
        {
            ZScoreDefaultTimeframe = "rolling_5d",
            ZScoreAddThreshold = 3.0,
            ZScoreReduceThreshold = -3.0,
        });
        return new ZScoreAdvisoryService(options, NullLogger<ZScoreAdvisoryService>.Instance);
    }
}
