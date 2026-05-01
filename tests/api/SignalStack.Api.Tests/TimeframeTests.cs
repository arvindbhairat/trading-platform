using SignalStack.Api.Historical;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Unit tests for the shared <see cref="Timeframe"/> type and <see cref="TimeframeService"/>.
///
/// REQ-TIMEFRAME-001: All six timeframes are defined.
/// REQ-TIMEFRAME-004: Timeframe resolution is consistent.
/// </summary>
public sealed class TimeframeTests
{
    // ── Timeframe static definitions ────────────────────────────────────

    [Fact]
    public void All_timeframes_includes_all_six_definitions()
    {
        Assert.Equal(6, Timeframe.All.Count);
        Assert.Contains(Timeframe.All, t => t.Name == "daily");
        Assert.Contains(Timeframe.All, t => t.Name == "weekly");
        Assert.Contains(Timeframe.All, t => t.Name == "monthly");
        Assert.Contains(Timeframe.All, t => t.Name == "rolling3");
        Assert.Contains(Timeframe.All, t => t.Name == "rolling5");
        Assert.Contains(Timeframe.All, t => t.Name == "rolling7");
    }

    [Fact]
    public void Standard_timeframes_are_not_rolling()
    {
        Assert.False(Timeframe.Daily.IsRolling);
        Assert.False(Timeframe.Weekly.IsRolling);
        Assert.False(Timeframe.Monthly.IsRolling);
    }

    [Fact]
    public void Rolling_timeframes_have_correct_window_sizes()
    {
        Assert.True(Timeframe.Rolling3.IsRolling);
        Assert.Equal(3, Timeframe.Rolling3.WindowSize);

        Assert.True(Timeframe.Rolling5.IsRolling);
        Assert.Equal(5, Timeframe.Rolling5.WindowSize);

        Assert.True(Timeframe.Rolling7.IsRolling);
        Assert.Equal(7, Timeframe.Rolling7.WindowSize);
    }

    [Fact]
    public void Rolling_timeframes_use_D_table_prefix()
    {
        Assert.Equal("D_", Timeframe.Rolling3.SqlTablePrefix);
        Assert.Equal("D_", Timeframe.Rolling5.SqlTablePrefix);
        Assert.Equal("D_", Timeframe.Rolling7.SqlTablePrefix);
    }

    [Fact]
    public void Weekly_and_monthly_use_correct_table_prefixes()
    {
        Assert.Equal("D_", Timeframe.Daily.SqlTablePrefix);
        Assert.Equal("W_", Timeframe.Weekly.SqlTablePrefix);
        Assert.Equal("M_", Timeframe.Monthly.SqlTablePrefix);
    }

    // ── TryParse ────────────────────────────────────────────────────────

    [Fact]
    public void TryParse_returns_correct_timeframe_for_full_names()
    {
        Assert.Same(Timeframe.Daily, Timeframe.TryParse("daily"));
        Assert.Same(Timeframe.Weekly, Timeframe.TryParse("weekly"));
        Assert.Same(Timeframe.Monthly, Timeframe.TryParse("monthly"));
        Assert.Same(Timeframe.Rolling3, Timeframe.TryParse("rolling3"));
        Assert.Same(Timeframe.Rolling5, Timeframe.TryParse("rolling5"));
        Assert.Same(Timeframe.Rolling7, Timeframe.TryParse("rolling7"));
    }

    [Fact]
    public void TryParse_is_case_insensitive()
    {
        Assert.Same(Timeframe.Daily, Timeframe.TryParse("DAILY"));
        Assert.Same(Timeframe.Daily, Timeframe.TryParse("Daily"));
        Assert.Same(Timeframe.Rolling3, Timeframe.TryParse("ROLLING3"));
    }

    [Fact]
    public void TryParse_returns_null_for_unknown_timeframe()
    {
        Assert.Null(Timeframe.TryParse("hourly"));
        Assert.Null(Timeframe.TryParse(""));
        Assert.Null(Timeframe.TryParse(null));
        Assert.Null(Timeframe.TryParse("intraday"));
    }

    // ── TimeframeService ───────────────────────────────────────────────

    [Fact]
    public void TimeframeService_Resolve_supports_standard_and_rolling()
    {
        var svc = new TimeframeService(new InMemoryTradingCalendarRepository());

        Assert.Same(Timeframe.Daily, svc.Resolve("daily"));
        Assert.Same(Timeframe.Rolling5, svc.Resolve("rolling5"));
        Assert.Null(svc.Resolve("unknown"));
    }

    [Fact]
    public void TimeframeService_Resolve_supports_short_aliases()
    {
        var svc = new TimeframeService(new InMemoryTradingCalendarRepository());

        Assert.Same(Timeframe.Daily, svc.Resolve("d"));
        Assert.Same(Timeframe.Weekly, svc.Resolve("w"));
        Assert.Same(Timeframe.Monthly, svc.Resolve("m"));
        Assert.Same(Timeframe.Rolling3, svc.Resolve("3"));
        Assert.Same(Timeframe.Rolling5, svc.Resolve("5"));
        Assert.Same(Timeframe.Rolling7, svc.Resolve("7"));
    }

    [Fact]
    public void TimeframeService_GetAll_returns_six_definitions()
    {
        var svc = new TimeframeService(new InMemoryTradingCalendarRepository());
        Assert.Equal(6, svc.GetAll().Count);
    }

    [Fact]
    public void TimeframeService_GetStandardTimeframes_returns_three()
    {
        var svc = new TimeframeService(new InMemoryTradingCalendarRepository());
        var standard = svc.GetStandardTimeframes();
        Assert.Equal(3, standard.Count);
        Assert.All(standard, t => Assert.False(t.IsRolling));
    }

    [Fact]
    public void TimeframeService_GetRollingTimeframes_returns_three()
    {
        var svc = new TimeframeService(new InMemoryTradingCalendarRepository());
        var rolling = svc.GetRollingTimeframes();
        Assert.Equal(3, rolling.Count);
        Assert.All(rolling, t => Assert.True(t.IsRolling));
    }

    // ── Parse ───────────────────────────────────────────────────────────

    [Fact]
    public void Parse_throws_for_unknown_timeframe()
    {
        Assert.Throws<ArgumentException>(() => Timeframe.Parse("hourly"));
    }

    [Fact]
    public void Parse_returns_correct_for_known_timeframe()
    {
        Assert.Same(Timeframe.Monthly, Timeframe.Parse("monthly"));
    }
}
