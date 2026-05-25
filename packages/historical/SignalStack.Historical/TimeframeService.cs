using SignalStack.Storage.Admin;

namespace SignalStack.Historical;

/// <summary>
/// Calendar-aware timeframe resolver and session-date provider.
///
/// REQ-TIMEFRAME-004: single source of truth for all timeframe resolution.
/// REQ-CALENDAR-003: session-aware date ranges backed by the trading calendar.
/// </summary>
public sealed class TimeframeService : ITimeframeService
{
    private readonly ITradingCalendarRepository _calendarRepo;

    public TimeframeService(ITradingCalendarRepository calendarRepo)
    {
        _calendarRepo = calendarRepo;
    }

    public IReadOnlyList<Timeframe> GetAll() => Timeframe.All;

    public IReadOnlyList<Timeframe> GetStandardTimeframes() =>
        Timeframe.All.Where(t => !t.IsRolling).ToList();

    public IReadOnlyList<Timeframe> GetRollingTimeframes() =>
        Timeframe.All.Where(t => t.IsRolling).ToList();

    public Timeframe? Resolve(string? name)
    {
        if (name is null) return null;

        // Try short aliases
        var alias = name.ToLowerInvariant() switch
        {
            "d" => "daily",
            "w" => "weekly",
            "m" => "monthly",
            "3" => "rolling3",
            "5" => "rolling5",
            "7" => "rolling7",
            _ => name
        };

        return Timeframe.TryParse(alias);
    }

    /// <summary>
    /// Returns the last N trading session dates ending at or before the given date,
    /// as determined by the NSE trading calendar.
    ///
    /// REQ-TIMEFRAME-002: rolling candles use trading sessions, not calendar dates.
    /// REQ-CALENDAR-003: the calendar is the shared authority for session boundaries.
    /// </summary>
    public async Task<List<DateOnly>> GetLastNSessionDatesAsync(int n, DateOnly to, CancellationToken ct = default)
    {
        if (n <= 0) return [];

        // Calculate a rough upper bound (N trading days could span weeks).
        var fromEstimate = to.AddDays(-(n * 2 + 30));

        var sessions = await _calendarRepo.GetSessionsAsync(
            fromDate: fromEstimate.ToString("yyyy-MM-dd"),
            toDate: to.ToString("yyyy-MM-dd"),
            ct: ct);

        return sessions
            .OrderByDescending(s => s.SessionDate)
            .Take(n)
            .Select(s => DateOnly.Parse(s.SessionDate))
            .OrderBy(d => d)
            .ToList();
    }
}
