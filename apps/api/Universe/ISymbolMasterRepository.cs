using MongoDB.Bson;

namespace SignalStack.Api.Universe;

/// <summary>
/// Repository for the <c>symbol_master</c> MongoDB collection.
/// Covers REQ-UNIV-001..010, REQ-HIST-003/004/008/008a.
/// </summary>
public interface ISymbolMasterRepository
{
    /// <summary>Returns all symbols, optionally filtered by archive state.</summary>
    Task<List<SymbolMasterDocument>> GetAllAsync(bool? archived = null, CancellationToken ct = default);

    /// <summary>Finds a symbol by its trading symbol. Returns null if not found.</summary>
    Task<SymbolMasterDocument?> FindBySymbolAsync(string symbol, CancellationToken ct = default);

    /// <summary>Finds a symbol by ISIN code. Returns null if not found.</summary>
    Task<SymbolMasterDocument?> FindByIsinAsync(string isin, CancellationToken ct = default);

    /// <summary>Finds a symbol by its SQL table name suffix. Returns null if not found.</summary>
    Task<SymbolMasterDocument?> FindBySuffixAsync(string suffix, CancellationToken ct = default);

    /// <summary>
    /// Returns all distinct <c>sql_table_name_suffix</c> values currently in the collection.
    /// Used by the Worker startup collision check (REQ-HIST-008a).
    /// </summary>
    Task<List<string>> GetAllSuffixesAsync(CancellationToken ct = default);

    /// <summary>Inserts a new symbol master document.</summary>
    Task CreateAsync(SymbolMasterDocument document, CancellationToken ct = default);

    /// <summary>
    /// Updates an existing symbol's mutable fields (industry, scan_excluded, is_archived, updated_at).
    /// Does NOT modify symbol, isin, sql_table_name_suffix, lot_size, company_name, series, or created_at.
    /// </summary>
    Task UpdateMetadataAsync(
        ObjectId id,
        string? industry = null,
        bool? isArchived = null,
        bool? scanExcluded = null,
        CancellationToken ct = default);

    /// <summary>
    /// Bulk-inserts multiple symbol master documents.
    /// Used during CSV import (REQ-UNIV-003..006).
    /// </summary>
    Task BulkCreateAsync(IEnumerable<SymbolMasterDocument> documents, CancellationToken ct = default);

    /// <summary>Returns the total count of symbols, optionally filtered by archive state.</summary>
    Task<long> CountAsync(bool? archived = null, CancellationToken ct = default);

    /// <summary>
    /// Checks whether any other document already uses the given symbol or ISIN.
    /// Returns true if a conflict exists.
    /// </summary>
    Task<bool> HasConflictAsync(string symbol, string isin, ObjectId? excludeId = null, CancellationToken ct = default);
}
