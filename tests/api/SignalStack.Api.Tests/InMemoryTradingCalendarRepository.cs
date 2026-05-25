using SignalStack.Api.Admin;
using SignalStack.Domain.Admin;
using SignalStack.Storage.Admin;

namespace SignalStack.Api.Tests;

/// <summary>
/// In-memory implementation of <see cref="ITradingCalendarRepository"/> for testing.
/// </summary>
public sealed class InMemoryTradingCalendarRepository : ITradingCalendarRepository
{
    private readonly List<TradingCalendarDocument> _entries = [];

    public void Seed(params TradingCalendarDocument[] entries)
    {
        _entries.Clear();
        _entries.AddRange(entries);
    }

    public Task<List<TradingCalendarDocument>> GetAllAsync(
        string? fromDate = null, string? toDate = null,
        string? sessionType = null, CancellationToken ct = default)
    {
        var query = _entries.AsEnumerable();

        if (fromDate is not null)
            query = query.Where(e => string.Compare(e.SessionDate, fromDate, StringComparison.Ordinal) >= 0);

        if (toDate is not null)
            query = query.Where(e => string.Compare(e.SessionDate, toDate, StringComparison.Ordinal) <= 0);

        if (sessionType is not null)
            query = query.Where(e => e.SessionType == sessionType);

        return Task.FromResult(query.OrderBy(e => e.SessionDate).ToList());
    }

    public Task<TradingCalendarDocument?> FindByDateAndTypeAsync(
        string sessionDate, string sessionType, CancellationToken ct = default) =>
        Task.FromResult(_entries.FirstOrDefault(e =>
            e.SessionDate == sessionDate && e.SessionType == sessionType));

    public Task<List<TradingCalendarDocument>> FindByDateAsync(
        string sessionDate, CancellationToken ct = default) =>
        Task.FromResult(_entries.Where(e => e.SessionDate == sessionDate).ToList());

    public Task<List<TradingCalendarDocument>> GetSessionsAsync(
        string? fromDate = null, string? toDate = null, CancellationToken ct = default)
    {
        var query = _entries.Where(e =>
            e.SessionType is "normal" or "special" or "muhurat");

        if (fromDate is not null)
            query = query.Where(e => string.Compare(e.SessionDate, fromDate, StringComparison.Ordinal) >= 0);

        if (toDate is not null)
            query = query.Where(e => string.Compare(e.SessionDate, toDate, StringComparison.Ordinal) <= 0);

        return Task.FromResult(query.OrderBy(e => e.SessionDate).ToList());
    }

    public Task<List<TradingCalendarDocument>> GetNonTradingDaysAsync(
        string? fromDate = null, string? toDate = null, CancellationToken ct = default)
    {
        var query = _entries.Where(e => e.SessionType == "non_trading_day");

        if (fromDate is not null)
            query = query.Where(e => string.Compare(e.SessionDate, fromDate, StringComparison.Ordinal) >= 0);

        if (toDate is not null)
            query = query.Where(e => string.Compare(e.SessionDate, toDate, StringComparison.Ordinal) <= 0);

        return Task.FromResult(query.OrderBy(e => e.SessionDate).ToList());
    }

    public Task CreateAsync(TradingCalendarDocument document, CancellationToken ct = default)
    {
        _entries.Add(document);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(
        MongoDB.Bson.ObjectId id,
        string? sessionType = null,
        string? sessionStartTime = null,
        string? sessionEndTime = null,
        string? holidayName = null,
        CancellationToken ct = default)
    {
        var entry = _entries.FirstOrDefault(e => e.Id == id);
        if (entry is null) return Task.CompletedTask;

        if (sessionType is not null) entry.SessionType = sessionType;
        if (sessionStartTime is not null) entry.SessionStartTime = sessionStartTime;
        if (sessionEndTime is not null) entry.SessionEndTime = sessionEndTime;
        if (holidayName is not null) entry.HolidayName = holidayName;
        entry.UpdatedAt = DateTime.UtcNow;

        return Task.CompletedTask;
    }

    public Task DeleteAsync(MongoDB.Bson.ObjectId id, CancellationToken ct = default)
    {
        _entries.RemoveAll(e => e.Id == id);
        return Task.CompletedTask;
    }

    public Task<long> CountAsync(
        string? fromDate = null, string? toDate = null,
        string? sessionType = null, CancellationToken ct = default)
    {
        var query = _entries.AsEnumerable();

        if (fromDate is not null)
            query = query.Where(e => string.Compare(e.SessionDate, fromDate, StringComparison.Ordinal) >= 0);

        if (toDate is not null)
            query = query.Where(e => string.Compare(e.SessionDate, toDate, StringComparison.Ordinal) <= 0);

        if (sessionType is not null)
            query = query.Where(e => e.SessionType == sessionType);

        return Task.FromResult((long)query.Count());
    }
}
