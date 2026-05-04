using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the Swing Low Stop Loss model (P6-T16).
/// (portfolio-risk-guidelines § Stop Loss Types — Swing Low Stop).
/// </summary>
public sealed class SwingLowStopLossTests
{
    // ─── Standard swing low stop calculation ──────────────────────────────────────

    [Fact]
    public void Calculate_uses_swing_low_price_for_stop_price()
    {
        // Arrange: entry = 1000; swing low = 920; buffer = 0%
        // stop = 920 * (1 - 0.0) = 920
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m, SwingLowPrice: 920m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(920m, result.StopPrice);
        Assert.Equal("SwingLowStop", result.StopTypeUsed);
    }

    // ─── Swing low with buffer ───────────────────────────────────────────────────

    [Fact]
    public void Calculate_applies_buffer_below_swing_low()
    {
        // Arrange: entry = 1000; swing low = 920; buffer = 0.5%
        // stop = 920 * (1 - 0.005) = 915.40
        var options = CreateOptions(swingLowBufferPct: 0.5);
        var model = CreateModel(options);
        var input = new StopLossInput(EntryPrice: 1000m, SwingLowPrice: 920m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(915.40m, result.StopPrice);
    }

    // ─── Explicit stop price from AdditionalInputs ───────────────────────────────

    [Fact]
    public void Calculate_uses_explicit_stop_price_from_additional_inputs()
    {
        // Arrange: entry = 1000; swing low = 920; explicit stop = 900 overrides swing low
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            SwingLowPrice: 920m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["SwingLowStopPrice"] = 900m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(900m, result.StopPrice);
    }

    // ─── Custom swing low buffer from options ────────────────────────────────────

    [Fact]
    public void Calculate_uses_custom_buffer_from_options()
    {
        // Arrange: entry = 1000; swing low = 920; buffer = 1.0%
        // stop = 920 * (1 - 0.01) = 910.80
        var options = CreateOptions(swingLowBufferPct: 1.0);
        var model = CreateModel(options);
        var input = new StopLossInput(EntryPrice: 1000m, SwingLowPrice: 920m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(910.80m, result.StopPrice);
    }

    // ─── Missing swing low -> fallback to default distance ───────────────────────

    [Fact]
    public void Calculate_falls_back_to_default_distance_when_swing_low_is_null()
    {
        // Arrange: entry = 1000; swing low = null; default distance = 5%
        // stop = 1000 * (1 - 0.05) = 950
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m, SwingLowPrice: null);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
        Assert.Equal("SwingLowStop", result.StopTypeUsed);
    }

    // ─── Zero swing low -> fallback to default distance ─────────────────────────

    [Fact]
    public void Calculate_falls_back_to_default_distance_when_swing_low_is_zero()
    {
        // Arrange: entry = 1000; swing low = 0; default distance = 5%
        // stop = 950
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m, SwingLowPrice: 0m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
    }

    // ─── Negative swing low -> fallback to default distance ──────────────────────

    [Fact]
    public void Calculate_falls_back_to_default_distance_when_swing_low_is_negative()
    {
        // Arrange: entry = 1000; swing low = -50; default distance = 5%
        // stop = 950
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m, SwingLowPrice: -50m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
    }

    // ─── Swing low at entry -> fallback to default distance ─────────────────────

    [Fact]
    public void Calculate_falls_back_when_swing_low_equals_entry()
    {
        // Arrange: entry = 1000; swing low = 1000 (same as entry); default distance = 5%
        // Stop must be below entry, so fallback to 5% distance = 950
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m, SwingLowPrice: 1000m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
    }

    // ─── Swing low above entry -> fallback to default distance ──────────────────

    [Fact]
    public void Calculate_falls_back_when_swing_low_is_above_entry()
    {
        // Arrange: entry = 800; swing low = 850 (above entry); default distance = 5%
        // stop = 800 * 0.95 = 760
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 800m, SwingLowPrice: 850m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(760m, result.StopPrice);
    }

    // ─── Larger buffer -> lower stop ────────────────────────────────────────────

    [Fact]
    public void Calculate_returns_lower_stop_for_larger_buffer()
    {
        // Arrange: entry = 1000; swing low = 950; buffer = 2.0%
        // stop = 950 * (1 - 0.02) = 931
        var options = CreateOptions(swingLowBufferPct: 2.0);
        var model = CreateModel(options);
        var input = new StopLossInput(EntryPrice: 1000m, SwingLowPrice: 950m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(931m, result.StopPrice);
    }

    // ─── Smaller buffer -> tighter stop ─────────────────────────────────────────

    [Fact]
    public void Calculate_returns_higher_stop_for_smaller_buffer()
    {
        // Arrange: entry = 1000; swing low = 950; buffer = 0.1%
        // stop = 950 * (1 - 0.001) = 949.05
        var options = CreateOptions(swingLowBufferPct: 0.1);
        var model = CreateModel(options);
        var input = new StopLossInput(EntryPrice: 1000m, SwingLowPrice: 950m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(949.05m, result.StopPrice);
    }

    // ─── Stop price clamped when at or above entry ──────────────────────────────

    [Fact]
    public void Calculate_clamps_stop_when_explicit_stop_is_above_entry()
    {
        // Arrange: entry = 500; swing low = 480; explicit stop = 550 (above entry)
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 500m,
            SwingLowPrice: 480m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["SwingLowStopPrice"] = 550m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.True(result.StopPrice < 500m);
        Assert.Equal("SwingLowStop", result.StopTypeUsed);
    }

    [Fact]
    public void Calculate_clamps_stop_when_fallback_equals_entry()
    {
        // Arrange: force fallback distance to 0% so fallback = entry, then clamp
        var options = CreateOptions(defaultDistancePct: 0.0);
        var model = CreateModel(options);
        var input = new StopLossInput(EntryPrice: 1000m, SwingLowPrice: null);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.True(result.StopPrice < 1000m, "Stop price must be clamped below entry");
    }

    // ─── Name property ─────────────────────────────────────────────────────────

    [Fact]
    public void Name_returns_SwingLowStop()
    {
        var model = CreateModel();
        Assert.Equal("SwingLowStop", model.Name);
    }

    // ─── Rounding to two decimal places ─────────────────────────────────────────

    [Fact]
    public void Calculate_rounds_stop_price_to_two_decimals()
    {
        // Arrange: entry = 1000; swing low = 912.345; buffer = 0%
        // stop = 912.345 -> rounded = 912.35 (standard rounding, away from zero)
        // Banker's rounding rounds 912.345 to 912.34 (rounds to even).
        // We use 912.346 to avoid the ambiguity.
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m, SwingLowPrice: 912.346m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(912.35m, result.StopPrice);
    }

    // ─── Large entry price ──────────────────────────────────────────────────────

    [Fact]
    public void Calculate_handles_large_entry_price()
    {
        // Arrange: entry = 25000; swing low = 23500; buffer = 0%
        // stop = 23500
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 25000m, SwingLowPrice: 23500m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(23500m, result.StopPrice);
    }

    // ─── AdditionalInputs with wrong type is ignored ────────────────────────────

    [Fact]
    public void Calculate_ignores_additional_input_with_wrong_type()
    {
        // Arrange: SwingLowStopPrice is a string instead of decimal; falls back to swing low calc
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            SwingLowPrice: 920m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["SwingLowStopPrice"] = "not-a-decimal"
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(920m, result.StopPrice); // swing low calc: 920
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static SwingLowStopLoss CreateModel(RmeModuleOptions? options = null)
    {
        options ??= CreateOptions();
        return new SwingLowStopLoss(
            Options.Create(options),
            NullLogger<SwingLowStopLoss>.Instance);
    }

    private static RmeModuleOptions CreateOptions(
        double defaultDistancePct = 5.0,
        double swingLowBufferPct = 0.0)
    {
        return new RmeModuleOptions
        {
            DefaultRiskPerTradePct = 0.5,
            MinRiskPerTradePct = 0.5,
            MaxRiskPerTradePct = 1.0,
            MaxActivePositions = 200,
            DefaultFixedStopDistancePct = defaultDistancePct,
            DefaultSwingLowBufferPct = swingLowBufferPct,
        };
    }
}
