using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the Break-Even Stop Loss model (P6-T17).
/// (portfolio-risk-guidelines § Stop Loss Types — Break-Even Stop).
/// </summary>
public sealed class BreakevenStopLossTests
{
    // ─── Breakeven triggered at 1R (default trigger) ──────────────────────────────

    [Fact]
    public void Calculate_triggers_breakeven_when_r_multiple_equals_trigger()
    {
        // Arrange: entry = 1000; current R = 1.0 (1R); trigger = 1.0 (default)
        // breakeven triggered -> stop = entry price = 1000
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = 1.0m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1000m, result.StopPrice);
        Assert.Equal("BreakevenStop", result.StopTypeUsed);
    }

    [Fact]
    public void Calculate_triggers_breakeven_when_r_multiple_exceeds_trigger()
    {
        // Arrange: entry = 500; current R = 2.5; trigger = 1.0 (default)
        // breakeven triggered -> stop = entry price = 500
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 500m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = 2.5m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(500m, result.StopPrice);
    }

    // ─── Custom trigger threshold ─────────────────────────────────────────────────

    [Fact]
    public void Calculate_uses_custom_trigger_from_options()
    {
        // Arrange: entry = 1000; current R = 1.5; trigger = 2.0 (custom)
        // 1.5 < 2.0 -> not triggered, fallback to 5% distance = 950
        var options = CreateOptions(breakevenTriggerR: 2.0);
        var model = CreateModel(options);
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = 1.5m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
    }

    [Fact]
    public void Calculate_triggers_at_custom_threshold()
    {
        // Arrange: entry = 1000; current R = 2.0; trigger = 2.0 (custom)
        // 2.0 >= 2.0 -> triggered, stop = entry price = 1000
        var options = CreateOptions(breakevenTriggerR: 2.0);
        var model = CreateModel(options);
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = 2.0m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1000m, result.StopPrice);
    }

    // ─── Fallback when not triggered ─────────────────────────────────────────────

    [Fact]
    public void Calculate_falls_back_when_r_multiple_below_trigger()
    {
        // Arrange: entry = 1000; current R = 0.5; trigger = 1.0 (default)
        // not triggered -> fallback to 5% distance = 950
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = 0.5m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
    }

    // ─── Fallback when R-multiple missing ─────────────────────────────────────────

    [Fact]
    public void Calculate_falls_back_when_current_r_multiple_not_provided()
    {
        // Arrange: entry = 1000; no R-multiple -> fallback to 5% distance = 950
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
    }

    // ─── Fallback when AdditionalInputs is null ───────────────────────────────────

    [Fact]
    public void Calculate_falls_back_when_additional_inputs_is_null()
    {
        // Arrange: entry = 1000; null additional inputs -> fallback to 5% = 950
        var model = CreateModel();
        var input = new StopLossInput(EntryPrice: 1000m, AdditionalInputs: null);

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
    }

    // ─── Custom fallback distance ─────────────────────────────────────────────────

    [Fact]
    public void Calculate_uses_custom_fallback_distance()
    {
        // Arrange: entry = 1000; current R = 0.3 (not triggered); custom distance = 3%
        // fallback: stop = 1000 * (1 - 0.03) = 970
        var options = CreateOptions(defaultDistancePct: 3.0);
        var model = CreateModel(options);
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = 0.3m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(970m, result.StopPrice);
    }

    // ─── Negative R-multiple (position in loss) ──────────────────────────────────

    [Fact]
    public void Calculate_falls_back_when_r_multiple_is_negative()
    {
        // Arrange: entry = 1000; current R = -0.5; trigger = 1.0
        // not triggered -> fallback to 5% = 950
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = -0.5m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
    }

    // ─── Clamping when fallback equals entry ─────────────────────────────────────

    [Fact]
    public void Calculate_clamps_fallback_when_distance_is_zero()
    {
        // Arrange: entry = 1000; current R = 0.5 (not triggered); distance = 0%
        // fallback = 1000 -> clamped to 990
        var options = CreateOptions(defaultDistancePct: 0.0);
        var model = CreateModel(options);
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = 0.5m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.True(result.StopPrice < 1000m, "Stop price must be clamped below entry");
    }

    // ─── Name property ───────────────────────────────────────────────────────────

    [Fact]
    public void Name_returns_BreakevenStop()
    {
        var model = CreateModel();
        Assert.Equal("BreakevenStop", model.Name);
    }

    // ─── Rounding to two decimal places ─────────────────────────────────────────

    [Fact]
    public void Calculate_rounds_breakeven_stop_price_to_two_decimals()
    {
        // Arrange: entry = 1000.1234; triggered at 1R -> stop = entry rounded
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000.1234m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = 1.5m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1000.12m, result.StopPrice);
    }

    [Fact]
    public void Calculate_rounds_fallback_stop_price_to_two_decimals()
    {
        // Arrange: entry = 1000.1234; not triggered; distance = 5%
        // fallback = 1000.1234 * 0.95 = 950.11723 -> 950.12
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000.1234m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = 0.3m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950.12m, result.StopPrice);
    }

    // ─── Large entry price ──────────────────────────────────────────────────────

    [Fact]
    public void Calculate_handles_large_entry_price_when_triggered()
    {
        // Arrange: entry = 25000; triggered at 1R
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 25000m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = 1.0m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(25000m, result.StopPrice);
    }

    // ─── AdditionalInputs with wrong type is ignored ────────────────────────────

    [Fact]
    public void Calculate_ignores_additional_input_with_wrong_type()
    {
        // Arrange: CurrentRMultiple is a string; falls back to default distance
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = "not-a-decimal"
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice); // fallback: 5% distance
    }

    // ─── Fine-grained R-multiple near trigger ───────────────────────────────────

    [Fact]
    public void Calculate_not_triggered_at_just_below_threshold()
    {
        // Arrange: entry = 1000; current R = 0.999; trigger = 1.0
        // 0.999 < 1.0 -> not triggered -> fallback to 5% = 950
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = 0.999m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(950m, result.StopPrice);
    }

    [Fact]
    public void Calculate_triggers_at_just_above_threshold()
    {
        // Arrange: entry = 1000; current R = 1.001; trigger = 1.0
        // 1.001 >= 1.0 -> triggered -> stop = 1000
        var model = CreateModel();
        var input = new StopLossInput(
            EntryPrice: 1000m,
            AdditionalInputs: new Dictionary<string, object>
            {
                ["CurrentRMultiple"] = 1.001m
            });

        // Act
        var result = model.Calculate(input);

        // Assert
        Assert.Equal(1000m, result.StopPrice);
    }

    // ─── Helpers ───────────────────────────────────────────────────────────────

    private static BreakevenStopLoss CreateModel(RmeModuleOptions? options = null)
    {
        options ??= CreateOptions();
        return new BreakevenStopLoss(
            Options.Create(options),
            NullLogger<BreakevenStopLoss>.Instance);
    }

    private static RmeModuleOptions CreateOptions(
        double defaultDistancePct = 5.0,
        double breakevenTriggerR = 1.0)
    {
        return new RmeModuleOptions
        {
            DefaultRiskPerTradePct = 0.5,
            MinRiskPerTradePct = 0.5,
            MaxRiskPerTradePct = 1.0,
            MaxActivePositions = 200,
            DefaultFixedStopDistancePct = defaultDistancePct,
            DefaultBreakevenTriggerR = breakevenTriggerR,
        };
    }
}
