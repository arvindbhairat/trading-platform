using Microsoft.Extensions.Logging.Abstractions;
using MongoDB.Bson;
using MongoDB.Driver;
using Moq;
using SignalStack.Api.Admin;
using SignalStack.Api.Notifications;
using Xunit;

namespace SignalStack.Api.Tests;

/// <summary>
/// Contract tests for Time Stop recompute (REQ-STOP-003/003a).
///
/// Verifies the calendar-aware date computation used to populate and
/// recompute <c>time_stop_date</c>.
/// </summary>
public sealed class TimeStopRecomputeTests
{
    /// <summary>
    /// Basic scenario: entry on a Friday, 10 sessions forward = 2 weeks later
    /// (weekends skipped).
    /// </summary>
    [Fact]
    public async Task ComputeTimeStopDateAsync_counts_forward_correctly_skipping_weekends()
    {
        var calendar = new InMemoryTradingCalendarRepository();
        SeedWeekdays(calendar, "2026-05-01", "2026-05-18");

        var svc = CreateService(calendar);

        // Entry: May 1 (Fri). 10 sessions forward = May 15 (Fri, 2 weeks later)
        var result = await svc.ComputeTimeStopDateAsync("2026-05-01", 10);
        Assert.Equal("2026-05-15", result);
    }

    /// <summary>
    /// REQ-STOP-003a edge case: a time-stop date lands on a date that was
    /// previously a normal trading session but has been changed to a
    /// non-trading-day. The recompute must roll forward to the next trading
    /// session.
    ///
    /// Scenario:
    ///   1. Calendar has sessions May 1–15 (Mon–Fri, no holidays).
    ///   2. Position entered May 1, count = 10 → time_stop_date = May 15 (Fri).
    ///   3. Admin changes May 14 from "normal" to "non_trading_day".
    ///   4. Recompute: the 10th session from May 1 is now May 18 (Mon).
    /// </summary>
    [Fact]
    public async Task ComputeTimeStopDateAsync_rolls_forward_when_date_becomes_holiday()
    {
        var calendar = new InMemoryTradingCalendarRepository();
        var docs = SeedWeekdays(calendar, "2026-05-01", "2026-05-18");

        var svc = CreateService(calendar);

        // Before the edit: 10 sessions = May 15
        var before = await svc.ComputeTimeStopDateAsync("2026-05-01", 10);
        Assert.Equal("2026-05-15", before);

        // Admin changes May 14 from "normal" to "non_trading_day".
        var may14 = docs.First(d => d.SessionDate == "2026-05-14");
        await calendar.UpdateAsync(
            may14.Id,
            sessionType: "non_trading_day",
            sessionStartTime: null,
            sessionEndTime: null,
            ct: CancellationToken.None);

        // After the edit: 10th session rolls forward to May 18 (Mon)
        var after = await svc.ComputeTimeStopDateAsync("2026-05-01", 10);
        Assert.Equal("2026-05-18", after);
    }

    /// <summary>
    /// When entry date falls on a weekend/non-trading-day, start counting
    /// from the next available trading session.
    /// </summary>
    [Fact]
    public async Task ComputeTimeStopDateAsync_starts_from_next_session_when_entry_is_nontrading()
    {
        var calendar = new InMemoryTradingCalendarRepository();
        SeedWeekdays(calendar, "2026-05-04", "2026-05-18"); // starts Mon May 4

        var svc = CreateService(calendar);

        // Entry: May 2 (Saturday — not a trading session).
        // First session after entry = May 4, count 5 forward = May 8
        var result = await svc.ComputeTimeStopDateAsync("2026-05-02", 5);
        Assert.Equal("2026-05-08", result);
    }

    /// <summary>
    /// A position with count 1 gets the next trading session after entry.
    /// </summary>
    [Fact]
    public async Task ComputeTimeStopDateAsync_session_count_1_returns_next_session()
    {
        var calendar = new InMemoryTradingCalendarRepository();
        SeedWeekdays(calendar, "2026-05-04", "2026-05-18");

        var svc = CreateService(calendar);

        var result = await svc.ComputeTimeStopDateAsync("2026-05-04", 1);
        Assert.Equal("2026-05-05", result);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Seed weekday sessions (Mon–Fri) in the given date range inclusive.
    /// Returns the list of created documents.
    /// </summary>
    private static List<TradingCalendarDocument> SeedWeekdays(
        InMemoryTradingCalendarRepository calendar, string fromDate, string toDate)
    {
        var from = DateOnly.Parse(fromDate);
        var to = DateOnly.Parse(toDate);
        var docs = new List<TradingCalendarDocument>();
        var idCounter = 0;

        for (var d = from; d <= to; d = d.AddDays(1))
        {
            if (d.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                continue;

            idCounter++;
            docs.Add(new TradingCalendarDocument
            {
                Id = ObjectId.GenerateNewId(),
                SessionDate = d.ToString("yyyy-MM-dd"),
                SessionType = "normal",
                SessionStartTime = "09:15",
                SessionEndTime = "15:30",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        calendar.Seed(docs.ToArray());
        return docs;
    }

    private static TimeStopRecomputeService CreateService(
        InMemoryTradingCalendarRepository calendar)
    {
        var mockDb = new Mock<IMongoDatabase>();
        var mockNotifWriter = new Mock<INotificationWriter>();

        return new TimeStopRecomputeService(
            mockDb.Object,
            calendar,
            mockNotifWriter.Object,
            NullLogger<TimeStopRecomputeService>.Instance);
    }
}
