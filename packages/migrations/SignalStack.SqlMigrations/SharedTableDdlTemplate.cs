using Microsoft.Data.SqlClient;

namespace SignalStack.SqlMigrations;

/// <summary>
/// Shared DDL template for per-symbol OHLCV tables (D_, W_, M_).
///
/// REQ-HIST-009a(c): the column definitions and index specifications for the three
/// per-symbol tables must be held as a single shared DDL template so that a future
/// change to the per-symbol table shape is a single edit.
///
/// This template is used by:
/// - HistoricDataSeed (HDS) when materialising tables for new symbols
/// - REQ-MIGRATION-004 batched migrations for fleet-wide schema evolution
///
/// REQ-HIST-002: each OHLCV table contains Date, Open, High, Low, Close, Volume.
/// This schema applies identically to D_, W_, and M_ tables.
/// </summary>
public static class SharedTableDdlTemplate
{
    /// <summary>
    /// Returns the CREATE TABLE DDL for a per-symbol OHLCV table with the given name.
    /// Uses IF NOT EXISTS semantics per REQ-HIST-009a(a).
    /// </summary>
    /// <param name="tableName">The fully-qualified table name (e.g. "D_RELIANCE").</param>
    /// <returns>The DDL string.</returns>
    public static string CreateTableIfNotExists(string tableName)
    {
        return $@"
IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_SCHEMA = 'dbo' AND TABLE_NAME = '{tableName}')
BEGIN
    CREATE TABLE [dbo].[{tableName}] (
        [Date]   DATE      NOT NULL,
        [Open]   FLOAT     NOT NULL,
        [High]   FLOAT     NOT NULL,
        [Low]    FLOAT     NOT NULL,
        [Close]  FLOAT     NOT NULL,
        [Volume] BIGINT    NOT NULL,

        CONSTRAINT [PK_{tableName}] PRIMARY KEY CLUSTERED ([Date])
    );

    CREATE INDEX [IX_{tableName}_Date] ON [dbo].[{tableName}] ([Date]);
END";
    }

    /// <summary>
    /// Creates the three per-symbol tables (D_, W_, M_) for the given suffix
    /// inside a single database transaction per REQ-HIST-009a(b).
    /// </summary>
    /// <param name="connection">An open SqlConnection.</param>
    /// <param name="sqlTableNameSuffix">The symbol's sql_table_name_suffix.</param>
    /// <param name="transaction">The database transaction for atomicity.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public static async Task CreatePerSymbolTablesAsync(
        SqlConnection connection,
        string sqlTableNameSuffix,
        SqlTransaction transaction,
        CancellationToken cancellationToken = default)
    {
        var dTable = $"D_{sqlTableNameSuffix}";
        var wTable = $"W_{sqlTableNameSuffix}";
        var mTable = $"M_{sqlTableNameSuffix}";

        foreach (var tableName in new[] { dTable, wTable, mTable })
        {
            var ddl = CreateTableIfNotExists(tableName);
            await using var cmd = new SqlCommand(ddl, connection, transaction);
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
    }
}
