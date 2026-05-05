using System.ComponentModel.DataAnnotations;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Validates that <see cref="RmeModuleOptions"/> data annotation constraints
/// are enforced at configuration binding time.
/// REQ-RME-001.
/// </summary>
public sealed class RmeModuleOptionsValidationTests
{
    [Fact]
    public void Default_options_are_valid()
    {
        var options = new RmeModuleOptions();
        var results = Validate(options);
        Assert.Empty(results);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(10_001)]
    public void MaxActivePositions_out_of_range_fails(int value)
    {
        var options = new RmeModuleOptions { MaxActivePositions = value };
        var results = Validate(options);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RmeModuleOptions.MaxActivePositions)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void DefaultRiskPerTradePct_out_of_range_fails(double value)
    {
        var options = new RmeModuleOptions { DefaultRiskPerTradePct = value };
        var results = Validate(options);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RmeModuleOptions.DefaultRiskPerTradePct)));
    }

    [Theory]
    [InlineData(0.1)]
    [InlineData(50.0)]
    [InlineData(100.0)]
    public void DefaultRiskPerTradePct_in_range_succeeds(double value)
    {
        var options = new RmeModuleOptions { DefaultRiskPerTradePct = value };
        var results = Validate(options);
        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(RmeModuleOptions.DefaultRiskPerTradePct)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void AtrMultiplier_out_of_range_fails(double value)
    {
        var options = new RmeModuleOptions { AtrMultiplier = value };
        var results = Validate(options);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RmeModuleOptions.AtrMultiplier)));
    }

    [Theory]
    [InlineData(0.5)]
    [InlineData(5.0)]
    [InlineData(10.0)]
    public void AtrMultiplier_in_range_succeeds(double value)
    {
        var options = new RmeModuleOptions { AtrMultiplier = value };
        var results = Validate(options);
        Assert.DoesNotContain(results, r => r.MemberNames.Contains(nameof(RmeModuleOptions.AtrMultiplier)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(101)]
    public void MaxPortfolioHeatPct_out_of_range_fails(double value)
    {
        var options = new RmeModuleOptions { MaxPortfolioHeatPct = value };
        var results = Validate(options);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RmeModuleOptions.MaxPortfolioHeatPct)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public void DefaultFixedStopDistancePct_out_of_range_fails(double value)
    {
        var options = new RmeModuleOptions { DefaultFixedStopDistancePct = value };
        var results = Validate(options);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RmeModuleOptions.DefaultFixedStopDistancePct)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(51)]
    public void DefaultTrailingStopPercent_out_of_range_fails(double value)
    {
        var options = new RmeModuleOptions { DefaultTrailingStopPercent = value };
        var results = Validate(options);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RmeModuleOptions.DefaultTrailingStopPercent)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(11)]
    public void DefaultSwingLowBufferPct_out_of_range_fails(double value)
    {
        var options = new RmeModuleOptions { DefaultSwingLowBufferPct = value };
        var results = Validate(options);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RmeModuleOptions.DefaultSwingLowBufferPct)));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    public void DefaultBreakevenTriggerR_out_of_range_fails(double value)
    {
        var options = new RmeModuleOptions { DefaultBreakevenTriggerR = value };
        var results = Validate(options);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RmeModuleOptions.DefaultBreakevenTriggerR)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void DrawdownReductionStartPct_out_of_range_fails(double value)
    {
        var options = new RmeModuleOptions { DrawdownReductionStartPct = value };
        var results = Validate(options);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RmeModuleOptions.DrawdownReductionStartPct)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(101)]
    public void DrawdownBlockPct_out_of_range_fails(double value)
    {
        var options = new RmeModuleOptions { DrawdownBlockPct = value };
        var results = Validate(options);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RmeModuleOptions.DrawdownBlockPct)));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(21)]
    public void MaxPyramidingAddonEntries_out_of_range_fails(int value)
    {
        var options = new RmeModuleOptions { MaxPyramidingAddonEntries = value };
        var results = Validate(options);
        Assert.Contains(results, r => r.MemberNames.Contains(nameof(RmeModuleOptions.MaxPyramidingAddonEntries)));
    }

    private static List<ValidationResult> Validate(RmeModuleOptions options)
    {
        var context = new ValidationContext(options);
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, context, results, validateAllProperties: true);
        return results;
    }
}
