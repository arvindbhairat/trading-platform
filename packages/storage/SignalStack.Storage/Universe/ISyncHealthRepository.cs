namespace SignalStack.Storage.Universe;

/// <summary>
/// Repository for checking data sync health of symbols against SQL Server D_ tables.
/// REQ-UNIV-015a: compares most recent daily candle date against the most recent
/// completed trading session from the internal trading calendar.
/// </summary>
public interface ISyncHealthRepository
{
    /// <summary>
    /// Returns sync health for the given symbol suffixes.
    /// For each suffix, queries the D_{suffix} table for the most recent Date.
    /// Returns null for lastCandleDate if the table doesn't exist or has no rows.
    /// </summary>
    Task<List<SymbolSyncHealth>> GetSyncHealthAsync(
        List<string> suffixes, CancellationToken ct = default);
}

/// <summary>Sync health for a single symbol.</summary>
public sealed record SymbolSyncHealth
{
    /// <summary>The sql_table_name_suffix (e.g. "RELIANCE", "TCS").</summary>
    public required string Suffix { get; init; }

    /// <summary>Most recent Date in the D_ table, or null if table missing / empty.</summary>
    public DateTime? LastCandleDate { get; init; }
}
