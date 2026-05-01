using System.Collections.Concurrent;
using MongoDB.Bson;
using SignalStack.Api.Universe;

namespace SignalStack.Api.Tests;

/// <summary>
/// In-memory <see cref="ISymbolMasterRepository"/> for integration tests.
/// REQ-UNIV-001..010, REQ-HIST-003/004/008/008a.
/// </summary>
public sealed class InMemorySymbolMasterRepository : ISymbolMasterRepository
{
    private readonly ConcurrentDictionary<string, SymbolMasterDocument> _bySymbol = new(StringComparer.OrdinalIgnoreCase);

    public Task<List<SymbolMasterDocument>> GetAllAsync(bool? archived = null, CancellationToken ct = default)
    {
        var query = _bySymbol.Values.AsEnumerable();
        if (archived.HasValue)
            query = query.Where(s => s.IsArchived == archived.Value);
        return Task.FromResult(query.OrderBy(s => s.Symbol).ToList());
    }

    public Task<SymbolMasterDocument?> FindBySymbolAsync(string symbol, CancellationToken ct = default)
    {
        _bySymbol.TryGetValue(symbol, out var doc);
        return Task.FromResult<SymbolMasterDocument?>(doc);
    }

    public Task<SymbolMasterDocument?> FindByIsinAsync(string isin, CancellationToken ct = default)
    {
        var doc = _bySymbol.Values.FirstOrDefault(
            s => string.Equals(s.Isin, isin, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<SymbolMasterDocument?>(doc);
    }

    public Task<SymbolMasterDocument?> FindBySuffixAsync(string suffix, CancellationToken ct = default)
    {
        var doc = _bySymbol.Values.FirstOrDefault(
            s => string.Equals(s.SqlTableNameSuffix, suffix, StringComparison.OrdinalIgnoreCase));
        return Task.FromResult<SymbolMasterDocument?>(doc);
    }

    public Task<List<string>> GetAllSuffixesAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_bySymbol.Values.Select(s => s.SqlTableNameSuffix).ToList());
    }

    public Task CreateAsync(SymbolMasterDocument document, CancellationToken ct = default)
    {
        if (!_bySymbol.TryAdd(document.Symbol, document))
            throw new InvalidOperationException($"Symbol '{document.Symbol}' already exists.");
        return Task.CompletedTask;
    }

    public Task UpdateMetadataAsync(
        ObjectId id,
        string? industry = null,
        bool? isArchived = null,
        bool? scanExcluded = null,
        CancellationToken ct = default)
    {
        var doc = _bySymbol.Values.FirstOrDefault(s => s.Id == id);
        if (doc is null)
            throw new InvalidOperationException($"Document with id '{id}' not found.");

        if (industry is not null) doc.Industry = industry;
        if (isArchived.HasValue) doc.IsArchived = isArchived.Value;
        if (scanExcluded.HasValue) doc.ScanExcluded = scanExcluded.Value;
        doc.UpdatedAt = DateTime.UtcNow;

        return Task.CompletedTask;
    }

    public Task BulkCreateAsync(IEnumerable<SymbolMasterDocument> documents, CancellationToken ct = default)
    {
        foreach (var doc in documents)
        {
            _bySymbol.TryAdd(doc.Symbol, doc);
        }
        return Task.CompletedTask;
    }

    public Task<long> CountAsync(bool? archived = null, CancellationToken ct = default)
    {
        var query = _bySymbol.Values.AsEnumerable();
        if (archived.HasValue)
            query = query.Where(s => s.IsArchived == archived.Value);
        return Task.FromResult((long)query.Count());
    }

    public Task<bool> HasConflictAsync(string symbol, string isin, ObjectId? excludeId = null, CancellationToken ct = default)
    {
        var hasConflict = _bySymbol.Values.Any(s =>
            (!excludeId.HasValue || s.Id != excludeId.Value) &&
            (string.Equals(s.Symbol, symbol, StringComparison.OrdinalIgnoreCase) ||
             string.Equals(s.Isin, isin, StringComparison.OrdinalIgnoreCase)));
        return Task.FromResult(hasConflict);
    }
}
