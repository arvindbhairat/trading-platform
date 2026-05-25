using System.Globalization;
using SignalStack.Storage.Historical;

namespace SignalStack.Historical;

/// <summary>
/// Single authoritative implementation of weekly candle boundary logic.
///
/// REQ-HIST-010: Weekly candles span from the first to the last trading session
/// of each ISO calendar week as defined by the internal NSE trading calendar.
/// DataSync is responsible for deriving and upserting these.
///
/// REQ-HIST-010a: Handles NSE special sessions including Muhurat trading sessions
/// that fall on Saturdays, Sundays, or public holidays, using explicit rules for:
///   (a) Week assignment by ISO 8601 (Asia/Kolkata).
///   (b) Single-session weeks (Muhurat on Saturday as the only session).
///   (c) Cross-week Muhurat sessions (Sunday assigned to prior week per ISO 8601).
///   (d) Consistency requirement: identical logic across DataSync, charting, backtesting.
///
/// REQ-HIST-010a(d): This is the single authoritative boundary implementation.
/// All three consumers (DataSync for W_ tables, charting layer for displayed
/// weekly candlestick charts, backtest engine for weekly-timeframe backtests)
/// must use this implementation. Divergence is a defect.
/// </summary>
public static class WeeklyCandleBoundary
{
    /// <summary>
    /// Returns the ISO 8601 week identifier for a session date in the Asia/Kolkata timezone.
    ///
    /// REQ-HIST-010a(a): A session's ISO calendar week is determined by the session date
    /// in Asia/Kolkata. Sessions on Saturday or Sunday (including Muhurat) are assigned
    /// to the ISO week that contains that date.
    /// </summary>
    /// <param name="sessionDate">The session date in Asia/Kolkata (already local).</param>
    /// <returns>(isoYear, isoWeek) tuple.</returns>
    public static (int Year, int Week) GetIsoWeek(DateOnly sessionDate)
    {
        var date = sessionDate.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var isoYear = ISOWeek.GetYear(date);
        var isoWeek = ISOWeek.GetWeekOfYear(date);
        return (isoYear, isoWeek);
    }

    /// <summary>
    /// Determines whether the given ISO week and year represent a valid weekly candle
    /// for a set of sessions grouped by ISO week.
    ///
    /// A valid weekly candle has at least one session. The candle is built from all
    /// sessions grouped under the same ISO week.
    ///
    /// REQ-HIST-010a(b): If an ISO week contains exactly one trading session, the weekly
    /// candle is built from that single session. This covers Muhurat sessions on a
    /// Saturday that are the only session in that ISO week.
    /// </summary>
    public static bool IsValidWeeklyCandle(int sessionCount) => sessionCount > 0;

    /// <summary>
    /// Computes the weekly OHLCV record from a list of daily OHLCV records
    /// that all belong to the same ISO week.
    ///
    /// The Open is the first session's Open, Close is the last session's Close,
    /// High is the max High across all sessions, Low is the min Low, and Volume
    /// is the sum.
    /// </summary>
    public static OhlcvRecord ComputeWeeklyCandle(IReadOnlyList<OhlcvRecord> weekSessions)
    {
        if (weekSessions.Count == 0)
            throw new ArgumentException("Cannot compute weekly candle from zero sessions.", nameof(weekSessions));

        var first = weekSessions[0];
        var last = weekSessions[^1];

        return new OhlcvRecord(
            Date: first.Date,
            Open: first.Open,
            High: weekSessions.Max(s => s.High),
            Low: weekSessions.Min(s => s.Low),
            Close: last.Close,
            Volume: weekSessions.Sum(s => s.Volume)
        );
    }

    /// <summary>
    /// Groups daily OHLCV records by ISO week and returns weekly candles.
    ///
    /// The sessions are expected to be in ascending date order.
    /// REQ-HIST-010a(c): Cross-week Muhurat sessions — if a session falls on a Sunday
    /// that ISO 8601 assigns to the following week, it is assigned to the prior week
    /// per ISO 8601. The standard ISOWeek.GetWeekOfYear() handles this correctly.
    /// </summary>
    /// <param name="dailyRecords">Daily OHLCV records sorted by date ascending.</param>
    /// <returns>Weekly OHLCV records, one per ISO week with at least one session.</returns>
    public static List<OhlcvRecord> AggregateToWeekly(IReadOnlyList<OhlcvRecord> dailyRecords)
    {
        if (dailyRecords.Count == 0) return [];

        // Group by ISO year + ISO week.
        var weekGroups = new List<(int IsoYear, int IsoWeek, List<OhlcvRecord> Records)>();
        List<OhlcvRecord>? currentGroup = null;
        int lastIsoYear = -1, lastIsoWeek = -1;

        foreach (var record in dailyRecords)
        {
            var (isoYear, isoWeek) = GetIsoWeek(record.Date);

            if (isoYear != lastIsoYear || isoWeek != lastIsoWeek)
            {
                if (currentGroup is not null)
                {
                    weekGroups.Add((lastIsoYear, lastIsoWeek, currentGroup));
                }
                currentGroup = [record];
                lastIsoYear = isoYear;
                lastIsoWeek = isoWeek;
            }
            else
            {
                currentGroup!.Add(record);
            }
        }

        // Don't forget the last group.
        if (currentGroup is not null)
        {
            weekGroups.Add((lastIsoYear, lastIsoWeek, currentGroup));
        }

        return weekGroups
            .Select(g => ComputeWeeklyCandle(g.Records))
            .ToList();
    }
}
