using MongoDB.Bson;

namespace SignalStack.Storage.Universe;

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

    /// <summary>
    /// Renames a symbol in place, preserving <c>sql_table_name_suffix</c> and all
    /// downstream references. REQ-UNIV-020 — approved rename path.
    /// </summary>
    Task RenameSymbolAsync(ObjectId id, string newSymbol, CancellationToken ct = default);

    /// <summary>
    /// Updates the symbol-validity-probe health tracking fields on a symbol master
    /// document. Only non-null parameters are applied. REQ-UNIV-021/021a.
    /// </summary>
    Task UpdateSymbolHealthAsync(
        ObjectId id,
        int? consecutiveFailureCount = null,
        DateTime? lastSuccessfulProbeAt = null,
        DateTime? lastUnknownSymbolAt = null,
        CancellationToken ct = default);

    /// <summary>
    /// Records that FYERS returned code -300 ("Invalid symbol provided") for this symbol.
    /// Sets <see cref="SymbolMasterDocument.FyersMarkedInvalidAt"/> to the given timestamp
    /// and increments <see cref="SymbolMasterDocument.ConsecutiveFailureCount"/> by 1.
    /// </summary>
    Task MarkFyersInvalidAsync(
        ObjectId id,
        DateTime timestamp,
        CancellationToken ct = default);

    /// <summary>
    /// Clears the FYERS invalid-symbol flag on this symbol.
    /// Sets <see cref="SymbolMasterDocument.FyersMarkedInvalidAt"/> to null and resets
    /// <see cref="SymbolMasterDocument.ConsecutiveFailureCount"/> to 0.
    /// Called when an admin resolves the FYERS-invalid work queue item.
    /// </summary>
    Task ClearFyersInvalidAsync(
        ObjectId id,
        CancellationToken ct = default);
}
