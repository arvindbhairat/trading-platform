using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Storage.Universe;

/// <summary>
/// Nifty 500 symbol definition stored in the MongoDB <c>symbol_master</c> collection.
/// REQ-UNIV-001..010, REQ-HIST-003/004/008.
/// </summary>
public sealed class SymbolMasterDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>Trading symbol on NSE (e.g. "RELIANCE", "TCS"). Unique.</summary>
    [BsonElement("symbol")]
    public required string Symbol { get; set; }

    /// <summary>Company name from the Nifty 500 CSV.</summary>
    [BsonElement("company_name")]
    public required string CompanyName { get; init; }

    /// <summary>Industry classification from the CSV. Canonical sector dimension.</summary>
    [BsonElement("industry")]
    public required string Industry { get; set; }

    /// <summary>NSE series (typically "EQ").</summary>
    [BsonElement("series")]
    public required string Series { get; init; }

    /// <summary>ISIN code — unique per security, continuity anchor for rename detection.</summary>
    [BsonElement("isin")]
    public required string Isin { get; init; }

    /// <summary>Stable SQL table name suffix generated per REQ-HIST-006/007. Immutable once assigned.</summary>
    [BsonElement("sql_table_name_suffix")]
    public required string SqlTableNameSuffix { get; init; }

    /// <summary>NSE minimum lot size for equity CNC orders. Default 1. REQ-UNIV-002.</summary>
    [BsonElement("lot_size")]
    public int LotSize { get; init; } = 1;

    /// <summary>True when the symbol has been removed from the active universe. REQ-UNIV-006.</summary>
    [BsonElement("is_archived")]
    public bool IsArchived { get; set; }

    /// <summary>True when the admin has temporarily excluded this symbol from scans. REQ-UNIV-009.</summary>
    [BsonElement("scan_excluded")]
    public bool ScanExcluded { get; set; }

    // ── Symbol Validity Probe health tracking (REQ-UNIV-021/021a) ──────────

    /// <summary>Consecutive unknown-symbol probe failures. Reset to 0 on success. REQ-UNIV-021a.</summary>
    [BsonElement("consecutive_failure_count")]
    public int ConsecutiveFailureCount { get; set; }

    /// <summary>UTC timestamp of the most recent successful probe. Null if never probed. REQ-UNIV-021a.</summary>
    [BsonElement("last_successful_probe_at")]
    [BsonIgnoreIfNull]
    public DateTime? LastSuccessfulProbeAt { get; set; }

    /// <summary>UTC timestamp of the most recent unknown-symbol probe failure. Null if none. REQ-UNIV-021a.</summary>
    [BsonElement("last_unknown_symbol_at")]
    [BsonIgnoreIfNull]
    public DateTime? LastUnknownSymbolAt { get; set; }

    [BsonElement("created_at")]
    public required DateTime CreatedAt { get; init; }

    [BsonElement("updated_at")]
    public DateTime UpdatedAt { get; set; }
}
