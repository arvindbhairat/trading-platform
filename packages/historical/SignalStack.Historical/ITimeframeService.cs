namespace SignalStack.Historical;

/// <summary>
/// Shared timeframe resolution service for charting, signals, backtesting,
/// and the EOD Signal Runner.
///
/// REQ-TIMEFRAME-004: Every consumer resolves timeframes through this service.
/// REQ-CALENDAR-003: Session-aware calculations use the trading calendar.
/// </summary>
public interface ITimeframeService
{
    /// <summary>Returns all supported timeframes in display order.</summary>
    IReadOnlyList<Timeframe> GetAll();

    /// <summary>Returns non-rolling timeframes only (daily, weekly, monthly).</summary>
    IReadOnlyList<Timeframe> GetStandardTimeframes();

    /// <summary>Returns rolling timeframes only (3R, 5R, 7R).</summary>
    IReadOnlyList<Timeframe> GetRollingTimeframes();

    /// <summary>
    /// Resolves a timeframe by name (case-insensitive).
    /// Supports short aliases: "d", "w", "m", "3", "5", "7".
    /// Returns null for unknown names.
    /// </summary>
    Timeframe? Resolve(string? name);

    /// <summary>
    /// Returns the last N trading session dates ending at or before the given date,
    /// using the trading calendar. REQ-TIMEFRAME-002/REQ-CALENDAR-003.
    /// </summary>
    Task<List<DateOnly>> GetLastNSessionDatesAsync(int n, DateOnly to, CancellationToken ct = default);
}
