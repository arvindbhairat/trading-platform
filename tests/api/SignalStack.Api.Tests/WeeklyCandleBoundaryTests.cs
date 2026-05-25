using SignalStack.Api.Historical;
using SignalStack.Historical;
using SignalStack.Storage.Historical;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Tests for <see cref="WeeklyCandleBoundary"/> — the single authoritative
/// weekly candle boundary implementation.
///
/// Covers REQ-HIST-010/010a, including Muhurat-on-Saturday handling.
/// REQ-HIST-010a(d): identical logic must be used by DataSync, charting, and backtesting.
/// </summary>
public sealed class WeeklyCandleBoundaryTests
{
    // ── ISO Week assignment (REQ-HIST-010a(a)) ─────────────────────────────

    [Fact]
    public void GetIsoWeek_returns_correct_iso_week_for_regular_tuesday()
    {
        // Tuesday, 2 Jan 2024 — ISO week 1 of 2024.
        var (year, week) = WeeklyCandleBoundary.GetIsoWeek(new DateOnly(2024, 1, 2));
        Assert.Equal(2024, year);
        Assert.Equal(1, week);
    }

    [Fact]
    public void GetIsoWeek_saturday_is_in_same_iso_week_as_preceding_monday()
    {
        // Saturday, 6 Jan 2024 — still ISO week 1 of 2024 (week runs Mon-Sun).
        var (year, week) = WeeklyCandleBoundary.GetIsoWeek(new DateOnly(2024, 1, 6));
        Assert.Equal(2024, year);
        Assert.Equal(1, week);
    }

    [Fact]
    public void GetIsoWeek_sunday_is_in_prior_iso_week()
    {
        // Sunday, 7 Jan 2024 — still ISO week 1 of 2024 (week 1 ends on Sunday).
        var (year, week) = WeeklyCandleBoundary.GetIsoWeek(new DateOnly(2024, 1, 7));
        Assert.Equal(2024, year);
        Assert.Equal(1, week);
    }

    // ── Week boundary at year transition ──────────────────────────────────

    [Fact]
    public void GetIsoWeek_new_year_monday_is_iso_week_1()
    {
        // Monday, 1 Jan 2024 — ISO week 1 of 2024.
        var (year, week) = WeeklyCandleBoundary.GetIsoWeek(new DateOnly(2024, 1, 1));
        Assert.Equal(2024, year);
        Assert.Equal(1, week);
    }

    // ── Single-session weeks (REQ-HIST-010a(b)) ───────────────────────────

    [Fact]
    public void IsValidWeeklyCandle_single_session_is_valid()
    {
        // A week with exactly one session (e.g. Muhurat on Saturday) is valid.
        Assert.True(WeeklyCandleBoundary.IsValidWeeklyCandle(1));
    }

    [Fact]
    public void IsValidWeeklyCandle_zero_sessions_is_invalid()
    {
        Assert.False(WeeklyCandleBoundary.IsValidWeeklyCandle(0));
    }

    // ── ComputeWeeklyCandle ─────────────────────────────────────────────────

    [Fact]
    public void ComputeWeeklyCandle_from_single_session_returns_equal_ohlcv()
    {
        // REQ-HIST-010a(b): single-session week — candle equals the daily record.
        var session = new OhlcvRecord(
            Date: new DateOnly(2024, 1, 6),
            Open: 2500.0,
            High: 2525.0,
            Low: 2480.0,
            Close: 2510.0,
            Volume: 100_000);

        var result = WeeklyCandleBoundary.ComputeWeeklyCandle([session]);

        Assert.Equal(session.Date, result.Date);
        Assert.Equal(session.Open, result.Open);
        Assert.Equal(session.High, result.High);
        Assert.Equal(session.Low, result.Low);
        Assert.Equal(session.Close, result.Close);
        Assert.Equal(session.Volume, result.Volume);
    }

    [Fact]
    public void ComputeWeeklyCandle_from_multiple_sessions_aggregates_correctly()
    {
        // A regular week with 5 sessions.
        var sessions = new List<OhlcvRecord>
        {
            new(new DateOnly(2024, 1, 1), 2400, 2420, 2390, 2410, 100_000),
            new(new DateOnly(2024, 1, 2), 2410, 2440, 2405, 2430, 120_000),
            new(new DateOnly(2024, 1, 3), 2430, 2460, 2420, 2450, 110_000),
            new(new DateOnly(2024, 1, 4), 2450, 2470, 2430, 2440, 90_000),
            new(new DateOnly(2024, 1, 5), 2440, 2480, 2435, 2470, 130_000),
        };

        var result = WeeklyCandleBoundary.ComputeWeeklyCandle(sessions);

        // Open = first session's Open, Close = last session's Close
        Assert.Equal(new DateOnly(2024, 1, 1), result.Date);
        Assert.Equal(2400, result.Open);
        Assert.Equal(2480, result.High);   // max High
        Assert.Equal(2390, result.Low);    // min Low
        Assert.Equal(2470, result.Close);  // last session's Close
        Assert.Equal(550_000, result.Volume); // sum
    }

    // ── AggregateToWeekly ───────────────────────────────────────────────────

    [Fact]
    public void AggregateToWeekly_empty_input_returns_empty()
    {
        var result = WeeklyCandleBoundary.AggregateToWeekly([]);
        Assert.Empty(result);
    }

    [Fact]
    public void AggregateToWeekly_single_week_returns_one_candle()
    {
        // Three sessions all in ISO week 1 of 2024.
        var records = new List<OhlcvRecord>
        {
            new(new DateOnly(2024, 1, 1), 2400, 2420, 2390, 2410, 100_000),
            new(new DateOnly(2024, 1, 2), 2410, 2440, 2405, 2430, 120_000),
            new(new DateOnly(2024, 1, 3), 2430, 2460, 2420, 2450, 110_000),
        };

        var result = WeeklyCandleBoundary.AggregateToWeekly(records);

        Assert.Single(result);
        Assert.Equal(2400, result[0].Open);
        Assert.Equal(2460, result[0].High);
        Assert.Equal(2390, result[0].Low);
        Assert.Equal(2450, result[0].Close);
        Assert.Equal(330_000, result[0].Volume);
    }

    [Fact]
    public void AggregateToWeekly_multi_week_returns_multiple_candles()
    {
        // Sessions spanning 3 weeks.
        var records = new List<OhlcvRecord>
        {
            // Week 1: Jan 1 (Mon) - Jan 5 (Fri)
            new(new DateOnly(2024, 1, 1), 2400, 2420, 2390, 2410, 100_000),
            new(new DateOnly(2024, 1, 5), 2410, 2440, 2405, 2430, 120_000),
            // Week 2: Jan 8 (Mon) - Jan 12 (Fri)
            new(new DateOnly(2024, 1, 8), 2440, 2460, 2420, 2450, 110_000),
            new(new DateOnly(2024, 1, 12), 2450, 2480, 2435, 2470, 130_000),
            // Week 3: Jan 15 (Mon) - Jan 19 (Fri)
            new(new DateOnly(2024, 1, 15), 2470, 2500, 2460, 2490, 90_000),
        };

        var result = WeeklyCandleBoundary.AggregateToWeekly(records);

        Assert.Equal(3, result.Count);
        Assert.Equal(new DateOnly(2024, 1, 1), result[0].Date); // Week 1 start
        Assert.Equal(new DateOnly(2024, 1, 8), result[1].Date); // Week 2 start
        Assert.Equal(new DateOnly(2024, 1, 15), result[2].Date); // Week 3 start
    }

    // ── Muhurat-on-Saturday single-session week (REQ-HIST-010a(b)) ──────────

    [Fact]
    public void AggregateToWeekly_muhurat_on_saturday_alone_creates_one_day_weekly_candle()
    {
        // Scenario: A Muhurat trading session on Saturday, 23 March 2024,
        // where all weekdays (Mon-Fri) were exchange holidays.
        //
        // Saturday 23 Mar 2024 is ISO week 12 of 2024.
        // (ISO week 12: Mon 18 Mar - Sun 24 Mar 2024)
        //
        // Since no other sessions exist in this ISO week, the weekly candle
        // is built from this single session per REQ-HIST-010a(b).

        var muhuratSession = new OhlcvRecord(
            Date: new DateOnly(2024, 3, 23),
            Open: 2500.0,
            High: 2525.0,
            Low: 2480.0,
            Close: 2510.0,
            Volume: 50_000);

        var result = WeeklyCandleBoundary.AggregateToWeekly([muhuratSession]);

        Assert.Single(result);
        Assert.Equal(2500.0, result[0].Open);
        Assert.Equal(2525.0, result[0].High);
        Assert.Equal(2480.0, result[0].Low);
        Assert.Equal(2510.0, result[0].Close);
        Assert.Equal(50_000, result[0].Volume);
    }

    // ── Cross-week Muhurat on Sunday (REQ-HIST-010a(c)) ─────────────────────

    [Fact]
    public void GetIsoWeek_sunday_belongs_to_prior_week_iso_8601()
    {
        // ISO 8601: weeks run Monday-Sunday.  Sunday is day 7 of the week
        // that started on the preceding Monday.
        //
        // Sunday, 24 Mar 2024: ISO week 12 of 2024 (same as Mon 18 Mar - Sat 23 Mar).
        var (year, week) = WeeklyCandleBoundary.GetIsoWeek(new DateOnly(2024, 3, 24));
        Assert.Equal(2024, year);
        Assert.Equal(12, week);
    }

    [Fact]
    public void AggregateToWeekly_sunday_session_is_grouped_with_prior_week()
    {
        // Sessions: Friday 22 Mar, Saturday 23 Mar (Muhurat), Sunday 24 Mar (Muhurat).
        // ISO week 12 of 2024: Mon 18 Mar - Sun 24 Mar.
        // All three should be in the same weekly candle.

        var records = new List<OhlcvRecord>
        {
            new(new DateOnly(2024, 3, 22), 2480, 2500, 2470, 2490, 80_000),
            new(new DateOnly(2024, 3, 23), 2490, 2525, 2480, 2510, 50_000),
            new(new DateOnly(2024, 3, 24), 2510, 2530, 2500, 2520, 40_000),
        };

        var result = WeeklyCandleBoundary.AggregateToWeekly(records);

        Assert.Single(result);
        Assert.Equal(new DateOnly(2024, 3, 22), result[0].Date);
        Assert.Equal(2480, result[0].Open);    // First session Open
        Assert.Equal(2530, result[0].High);    // Max High
        Assert.Equal(2470, result[0].Low);     // Min Low
        Assert.Equal(2520, result[0].Close);   // Last session Close
        Assert.Equal(170_000, result[0].Volume); // Sum
    }

    // ── Year-boundary Sunday (REQ-HIST-010a(c) edge case) ──────────────────

    [Fact]
    public void GetIsoWeek_dec31_sunday_belongs_to_prior_iso_year()
    {
        // Sunday, 31 Dec 2023.
        // In ISO 8601, this is week 52 of 2023 (the week that started Mon 25 Dec 2023).
        var (year, week) = WeeklyCandleBoundary.GetIsoWeek(new DateOnly(2023, 12, 31));
        Assert.Equal(2023, year);
        Assert.Equal(52, week);
    }
}
