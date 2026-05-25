using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Storage.Universe;

/// <summary>
/// Audit record for each admin CSV upload — stored in MongoDB <c>universe_upload_results</c>.
/// REQ-UNIV-011..020.
/// </summary>
public sealed class UniverseUploadResultDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>Admin user who submitted the upload.</summary>
    [BsonElement("uploaded_by")]
    public required string UploadedBy { get; init; }

    /// <summary>UTC timestamp of the upload commit.</summary>
    [BsonElement("uploaded_at")]
    public required DateTime UploadedAt { get; init; }

    /// <summary>SHA-256 hash of the uploaded CSV content.</summary>
    [BsonElement("file_hash")]
    public required string FileHash { get; init; }

    /// <summary>Total rows in the parsed CSV (after header).</summary>
    [BsonElement("total_rows")]
    public int TotalRows { get; init; }

    /// <summary>Rows accepted (Series == EQ).</summary>
    [BsonElement("rows_accepted")]
    public int RowsAccepted { get; init; }

    /// <summary>Rows archived (in current master, absent from upload, not resolved as rename).</summary>
    [BsonElement("rows_archived")]
    public int RowsArchived { get; init; }

    /// <summary>Rows excluded because Series != EQ.</summary>
    [BsonElement("rows_excluded")]
    public int RowsExcluded { get; init; }

    /// <summary>Symbols that were net-new additions.</summary>
    [BsonElement("new_symbols")]
    public List<string> NewSymbols { get; init; } = [];

    /// <summary>Symbols that were archived by this upload.</summary>
    [BsonElement("archived_symbols")]
    public List<string> ArchivedSymbols { get; init; } = [];

    /// <summary>Symbols whose Industry changed, with before/after values.</summary>
    [BsonElement("industry_changes")]
    public List<IndustryChangeEntry> IndustryChanges { get; init; } = [];

    /// <summary>Rows skipped with Series != EQ.</summary>
    [BsonElement("skipped_rows")]
    public List<SkippedRowEntry> SkippedRows { get; init; } = [];

    /// <summary>Rename resolutions approved (old → new, shared ISIN).</summary>
    [BsonElement("rename_resolutions")]
    public List<RenameResolutionEntry> RenameResolutions { get; init; } = [];

    /// <summary>Whether HistoricDataSeed was auto-triggered for net-new symbols.</summary>
    [BsonElement("hds_triggered")]
    public bool HdsTriggered { get; init; }

    /// <summary>True if this upload has been rolled back.</summary>
    [BsonElement("rolled_back")]
    public bool RolledBack { get; set; }

    /// <summary>UTC timestamp of rollback, if any.</summary>
    [BsonElement("rolled_back_at")]
    public DateTime? RolledBackAt { get; set; }

    /// <summary>Admin who performed the rollback.</summary>
    [BsonElement("rolled_back_by")]
    public string? RolledBackBy { get; set; }

    /// <summary>
    /// Pre-upload snapshot of the symbol master for rollback.
    /// Retained for <c>operations.universe.rollback_retention_days</c>.
    /// </summary>
    [BsonElement("snapshot")]
    public required UniverseSnapshot Snapshot { get; init; }
}

/// <summary>Pre-upload snapshot of the full symbol master for rollback. REQ-UNIV-019.</summary>
public sealed class UniverseSnapshot
{
    /// <summary>Serialised state of every symbol master document at commit time.</summary>
    [BsonElement("symbols")]
    public required List<SymbolSnapshotEntry> Symbols { get; init; }
}

/// <summary>Serialised state of a single symbol at snapshot time.</summary>
public sealed class SymbolSnapshotEntry
{
    [BsonElement("symbol")]
    public required string Symbol { get; init; }

    [BsonElement("company_name")]
    public required string CompanyName { get; init; }

    [BsonElement("industry")]
    public required string Industry { get; init; }

    [BsonElement("series")]
    public required string Series { get; init; }

    [BsonElement("isin")]
    public required string Isin { get; init; }

    [BsonElement("sql_table_name_suffix")]
    public required string SqlTableNameSuffix { get; init; }

    [BsonElement("lot_size")]
    public int LotSize { get; init; } = 1;

    [BsonElement("is_archived")]
    public bool IsArchived { get; init; }

    [BsonElement("scan_excluded")]
    public bool ScanExcluded { get; init; }
}

/// <summary>Industry reclassification recorded by a CSV upload. REQ-UNIV-002a.</summary>
public sealed class IndustryChangeEntry
{
    [BsonElement("symbol")]
    public required string Symbol { get; init; }

    [BsonElement("previous_industry")]
    public required string PreviousIndustry { get; init; }

    [BsonElement("new_industry")]
    public required string NewIndustry { get; init; }
}

/// <summary>A skipped row from the CSV upload. REQ-UNIV-013.</summary>
public sealed class SkippedRowEntry
{
    [BsonElement("symbol")]
    public required string Symbol { get; init; }

    [BsonElement("company_name")]
    public required string CompanyName { get; init; }

    [BsonElement("series")]
    public required string Series { get; init; }

    [BsonElement("reason")]
    public required string Reason { get; init; }
}

/// <summary>A rename resolution recorded by a CSV upload. REQ-UNIV-020.</summary>
public sealed class RenameResolutionEntry
{
    [BsonElement("old_symbol")]
    public required string OldSymbol { get; init; }

    [BsonElement("new_symbol")]
    public required string NewSymbol { get; init; }

    [BsonElement("isin")]
    public required string Isin { get; init; }

    [BsonElement("resolution")]
    public required string Resolution { get; init; } // "approved" or "rejected"
}
