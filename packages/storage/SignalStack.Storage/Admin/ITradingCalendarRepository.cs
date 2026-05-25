using MongoDB.Bson;
using SignalStack.Domain.Admin;

namespace SignalStack.Storage.Admin;

/// <summary>
/// Repository for the <c>trading_calendar</c> MongoDB collection.
/// REQ-CALENDAR-001..006.
/// </summary>
public interface ITradingCalendarRepository
{
    /// <summary>Returns all calendar entries, optionally filtered by date range and session type.</summary>
    Task<List<TradingCalendarDocument>> GetAllAsync(
        string? fromDate = null, string? toDate = null,
        string? sessionType = null, CancellationToken ct = default);

    /// <summary>Finds a calendar entry by session date and type. Returns null if not found.</summary>
    Task<TradingCalendarDocument?> FindByDateAndTypeAsync(
        string sessionDate, string sessionType, CancellationToken ct = default);

    /// <summary>Finds calendar entries for a specific date across all types.</summary>
    Task<List<TradingCalendarDocument>> FindByDateAsync(
        string sessionDate, CancellationToken ct = default);

    /// <summary>Returns all session records (normal/special/muhurat) within a date range.</summary>
    Task<List<TradingCalendarDocument>> GetSessionsAsync(
        string? fromDate = null, string? toDate = null, CancellationToken ct = default);

    /// <summary>
    /// Returns non-trading-day markers within a date range.
    /// REQ-CALENDAR-002: these carry no session times and explicitly signal exchange closure.
    /// </summary>
    Task<List<TradingCalendarDocument>> GetNonTradingDaysAsync(
        string? fromDate = null, string? toDate = null, CancellationToken ct = default);

    /// <summary>Creates a new calendar entry.</summary>
    Task CreateAsync(TradingCalendarDocument document, CancellationToken ct = default);

    /// <summary>Updates an existing calendar entry's mutable fields.</summary>
    Task UpdateAsync(
        ObjectId id,
        string? sessionType = null,
        string? sessionStartTime = null,
        string? sessionEndTime = null,
        string? holidayName = null,
        CancellationToken ct = default);

    /// <summary>Deletes a calendar entry by ID.</summary>
    Task DeleteAsync(ObjectId id, CancellationToken ct = default);

    /// <summary>Returns the count of entries within a date range.</summary>
    Task<long> CountAsync(
        string? fromDate = null, string? toDate = null,
        string? sessionType = null, CancellationToken ct = default);
}
