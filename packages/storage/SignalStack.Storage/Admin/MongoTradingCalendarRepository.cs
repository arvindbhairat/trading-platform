using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Domain.Admin;

namespace SignalStack.Storage.Admin;

public sealed class MongoTradingCalendarRepository : ITradingCalendarRepository
{
    private readonly IMongoCollection<TradingCalendarDocument> _collection;

    public MongoTradingCalendarRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<TradingCalendarDocument>("trading_calendar");
    }

    public async Task<List<TradingCalendarDocument>> GetAllAsync(
        string? fromDate = null, string? toDate = null,
        string? sessionType = null, CancellationToken ct = default)
    {
        var filters = new List<FilterDefinition<TradingCalendarDocument>>();

        if (fromDate is not null)
            filters.Add(Builders<TradingCalendarDocument>.Filter.Gte(d => d.SessionDate, fromDate));

        if (toDate is not null)
            filters.Add(Builders<TradingCalendarDocument>.Filter.Lte(d => d.SessionDate, toDate));

        if (sessionType is not null)
            filters.Add(Builders<TradingCalendarDocument>.Filter.Eq(d => d.SessionType, sessionType));

        var filter = filters.Count > 0
            ? Builders<TradingCalendarDocument>.Filter.And(filters)
            : Builders<TradingCalendarDocument>.Filter.Empty;

        return await _collection
            .Find(filter)
            .Sort(Builders<TradingCalendarDocument>.Sort.Ascending(d => d.SessionDate))
            .ToListAsync(ct);
    }

    public async Task<TradingCalendarDocument?> FindByDateAndTypeAsync(
        string sessionDate, string sessionType, CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<TradingCalendarDocument>.Filter.And(
                Builders<TradingCalendarDocument>.Filter.Eq(d => d.SessionDate, sessionDate),
                Builders<TradingCalendarDocument>.Filter.Eq(d => d.SessionType, sessionType)))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<TradingCalendarDocument>> FindByDateAsync(
        string sessionDate, CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<TradingCalendarDocument>.Filter.Eq(d => d.SessionDate, sessionDate))
            .ToListAsync(ct);
    }

    public async Task<List<TradingCalendarDocument>> GetSessionsAsync(
        string? fromDate = null, string? toDate = null, CancellationToken ct = default)
    {
        var filters = new List<FilterDefinition<TradingCalendarDocument>>
        {
            Builders<TradingCalendarDocument>.Filter.In(
                d => d.SessionType, ["normal", "special", "muhurat"])
        };

        if (fromDate is not null)
            filters.Add(Builders<TradingCalendarDocument>.Filter.Gte(d => d.SessionDate, fromDate));

        if (toDate is not null)
            filters.Add(Builders<TradingCalendarDocument>.Filter.Lte(d => d.SessionDate, toDate));

        return await _collection
            .Find(Builders<TradingCalendarDocument>.Filter.And(filters))
            .Sort(Builders<TradingCalendarDocument>.Sort.Ascending(d => d.SessionDate))
            .ToListAsync(ct);
    }

    public async Task<List<TradingCalendarDocument>> GetNonTradingDaysAsync(
        string? fromDate = null, string? toDate = null, CancellationToken ct = default)
    {
        var filters = new List<FilterDefinition<TradingCalendarDocument>>
        {
            Builders<TradingCalendarDocument>.Filter.Eq(d => d.SessionType, "non_trading_day")
        };

        if (fromDate is not null)
            filters.Add(Builders<TradingCalendarDocument>.Filter.Gte(d => d.SessionDate, fromDate));

        if (toDate is not null)
            filters.Add(Builders<TradingCalendarDocument>.Filter.Lte(d => d.SessionDate, toDate));

        return await _collection
            .Find(Builders<TradingCalendarDocument>.Filter.And(filters))
            .Sort(Builders<TradingCalendarDocument>.Sort.Ascending(d => d.SessionDate))
            .ToListAsync(ct);
    }

    public async Task CreateAsync(TradingCalendarDocument document, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(document, cancellationToken: ct);
    }

    public async Task UpdateAsync(
        ObjectId id,
        string? sessionType = null,
        string? sessionStartTime = null,
        string? sessionEndTime = null,
        string? holidayName = null,
        CancellationToken ct = default)
    {
        var filter = Builders<TradingCalendarDocument>.Filter.Eq(d => d.Id, id);

        var updateDefs = new List<UpdateDefinition<TradingCalendarDocument>>
        {
            Builders<TradingCalendarDocument>.Update.Set(d => d.UpdatedAt, DateTime.UtcNow)
        };

        if (sessionType is not null)
            updateDefs.Add(Builders<TradingCalendarDocument>.Update.Set(d => d.SessionType, sessionType));

        if (sessionStartTime is not null)
            updateDefs.Add(Builders<TradingCalendarDocument>.Update.Set(d => d.SessionStartTime, sessionStartTime));

        if (sessionEndTime is not null)
            updateDefs.Add(Builders<TradingCalendarDocument>.Update.Set(d => d.SessionEndTime, sessionEndTime));

        if (holidayName is not null)
            updateDefs.Add(Builders<TradingCalendarDocument>.Update.Set(d => d.HolidayName, holidayName));

        var combined = Builders<TradingCalendarDocument>.Update.Combine(updateDefs);
        await _collection.UpdateOneAsync(filter, combined, cancellationToken: ct);
    }

    public async Task DeleteAsync(ObjectId id, CancellationToken ct = default)
    {
        await _collection.DeleteOneAsync(
            Builders<TradingCalendarDocument>.Filter.Eq(d => d.Id, id), ct);
    }

    public async Task<long> CountAsync(
        string? fromDate = null, string? toDate = null,
        string? sessionType = null, CancellationToken ct = default)
    {
        var filters = new List<FilterDefinition<TradingCalendarDocument>>();

        if (fromDate is not null)
            filters.Add(Builders<TradingCalendarDocument>.Filter.Gte(d => d.SessionDate, fromDate));

        if (toDate is not null)
            filters.Add(Builders<TradingCalendarDocument>.Filter.Lte(d => d.SessionDate, toDate));

        if (sessionType is not null)
            filters.Add(Builders<TradingCalendarDocument>.Filter.Eq(d => d.SessionType, sessionType));

        var filter = filters.Count > 0
            ? Builders<TradingCalendarDocument>.Filter.And(filters)
            : Builders<TradingCalendarDocument>.Filter.Empty;

        return await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }
}
