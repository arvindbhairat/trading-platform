using MongoDB.Bson;
using MongoDB.Driver;

namespace SignalStack.Api.Universe;

public sealed class MongoSymbolMasterRepository : ISymbolMasterRepository
{
    private readonly IMongoCollection<SymbolMasterDocument> _collection;

    public MongoSymbolMasterRepository(IMongoDatabase database)
    {
        _collection = database.GetCollection<SymbolMasterDocument>("symbol_master");
    }

    public async Task<List<SymbolMasterDocument>> GetAllAsync(
        bool? archived = null, CancellationToken ct = default)
    {
        var filter = archived.HasValue
            ? Builders<SymbolMasterDocument>.Filter.Eq(s => s.IsArchived, archived.Value)
            : Builders<SymbolMasterDocument>.Filter.Empty;

        return await _collection
            .Find(filter)
            .Sort(Builders<SymbolMasterDocument>.Sort.Ascending(s => s.Symbol))
            .ToListAsync(ct);
    }

    public async Task<SymbolMasterDocument?> FindBySymbolAsync(
        string symbol, CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<SymbolMasterDocument>.Filter.Eq(s => s.Symbol, symbol))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<SymbolMasterDocument?> FindByIsinAsync(
        string isin, CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<SymbolMasterDocument>.Filter.Eq(s => s.Isin, isin))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<SymbolMasterDocument?> FindBySuffixAsync(
        string suffix, CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<SymbolMasterDocument>.Filter.Eq(s => s.SqlTableNameSuffix, suffix))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<List<string>> GetAllSuffixesAsync(CancellationToken ct = default)
    {
        return await _collection
            .Find(Builders<SymbolMasterDocument>.Filter.Empty)
            .Project(s => s.SqlTableNameSuffix)
            .ToListAsync(ct);
    }

    public async Task CreateAsync(SymbolMasterDocument document, CancellationToken ct = default)
    {
        await _collection.InsertOneAsync(document, cancellationToken: ct);
    }

    public async Task UpdateMetadataAsync(
        ObjectId id,
        string? industry = null,
        bool? isArchived = null,
        bool? scanExcluded = null,
        CancellationToken ct = default)
    {
        var filter = Builders<SymbolMasterDocument>.Filter.Eq(s => s.Id, id);

        var updateDefs = new List<UpdateDefinition<SymbolMasterDocument>>
        {
            Builders<SymbolMasterDocument>.Update.Set(s => s.UpdatedAt, DateTime.UtcNow)
        };

        if (industry is not null)
            updateDefs.Add(Builders<SymbolMasterDocument>.Update.Set(s => s.Industry, industry));

        if (isArchived.HasValue)
            updateDefs.Add(Builders<SymbolMasterDocument>.Update.Set(s => s.IsArchived, isArchived.Value));

        if (scanExcluded.HasValue)
            updateDefs.Add(Builders<SymbolMasterDocument>.Update.Set(s => s.ScanExcluded, scanExcluded.Value));

        var combined = Builders<SymbolMasterDocument>.Update.Combine(updateDefs);
        await _collection.UpdateOneAsync(filter, combined, cancellationToken: ct);
    }

    public async Task BulkCreateAsync(
        IEnumerable<SymbolMasterDocument> documents, CancellationToken ct = default)
    {
        await _collection.InsertManyAsync(documents, cancellationToken: ct);
    }

    public async Task<long> CountAsync(bool? archived = null, CancellationToken ct = default)
    {
        var filter = archived.HasValue
            ? Builders<SymbolMasterDocument>.Filter.Eq(s => s.IsArchived, archived.Value)
            : Builders<SymbolMasterDocument>.Filter.Empty;

        return await _collection.CountDocumentsAsync(filter, cancellationToken: ct);
    }

    public async Task<bool> HasConflictAsync(
        string symbol, string isin, ObjectId? excludeId = null, CancellationToken ct = default)
    {
        var filters = new List<FilterDefinition<SymbolMasterDocument>>
        {
            Builders<SymbolMasterDocument>.Filter.Or(
                Builders<SymbolMasterDocument>.Filter.Eq(s => s.Symbol, symbol),
                Builders<SymbolMasterDocument>.Filter.Eq(s => s.Isin, isin))
        };

        if (excludeId.HasValue)
            filters.Add(Builders<SymbolMasterDocument>.Filter.Ne(s => s.Id, excludeId.Value));

        var combined = Builders<SymbolMasterDocument>.Filter.And(filters);
        return await _collection.Find(combined).AnyAsync(ct);
    }
}
