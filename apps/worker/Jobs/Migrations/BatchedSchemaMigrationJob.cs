using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using SignalStack.Api.Historical;
using SignalStack.Api.Universe;
using SignalStack.Migrations;

namespace SignalStack.Worker.Jobs.Migrations;

/// <summary>
/// Base class for fleet-wide batched per-symbol schema migrations
/// (REQ-MIGRATION-004/005).
///
/// A batched migration applies a DDL change to every existing per-symbol
/// table (D_, W_, M_) across the full symbol master.  It respects the
/// dual-read window (REQ-MIGRATION-005), is resumable on interruption,
/// and writes progress tracking to the <c>job_runs</c> collection.
///
/// # Contract for derived classes
///
/// 1. Implement <see cref="GetDdlAsync"/> to return the DDL statement to
///    execute per table, parameterised with the table name.
///
/// 2. Optionally override <see cref="GetDdlPostStatementAsync"/> to return a
///    statement to run after the per-table DDL (e.g. an index creation that
///    references the table).
///
/// 3. Optionally override <see cref="OnBeforeSymbolBatchAsync"/> and
///    <see cref="OnAfterSymbolBatchAsync"/> to run setup/teardown before or
///    after the full batch.
///
/// 4. Override <see cref="MigrationId"/> to return a stable identifier for
///    this migration (used in job_runs and logging).
///
/// # Dual-read window semantics
///
/// When the dual-read window is active (see <see cref="DualReadWindowService"/>),
/// the job logs a warning that callers should accept both old and new schemas
/// during the window.  The decision to execute the DDL is unaffected — the
/// schema change itself applies identically.  The dual-read window governs
/// *application-code read paths*, not DDL execution.
///
/// # Resumability
///
/// The job tracks completion per symbol in job_runs.progress.completed_symbols.
/// On restart, already-completed symbols are skipped.  The DDL uses IF NOT EXISTS
/// / safe-add-column semantics where possible so that partial execution is safe.
/// </summary>
public abstract class BatchedSchemaMigrationJob
{
    private readonly string _sqlConnectionString;
    private readonly ISymbolTableMapping _tableMapping;
    private readonly ISymbolMasterRepository _symbolMasterRepo;
    private readonly DualReadWindowService _dualReadWindow;
    private readonly ILogger _logger;

    protected BatchedSchemaMigrationJob(
        string sqlConnectionString,
        ISymbolTableMapping tableMapping,
        ISymbolMasterRepository symbolMasterRepo,
        DualReadWindowService dualReadWindow,
        ILogger logger)
    {
        _sqlConnectionString = sqlConnectionString;
        _tableMapping = tableMapping;
        _symbolMasterRepo = symbolMasterRepo;
        _dualReadWindow = dualReadWindow;
        _logger = logger;
    }

    /// <summary>
    /// Stable identifier for this migration, used in job_runs tracking and logs.
    /// Example: "MIG-v2-add-sma-column".
    /// </summary>
    protected abstract string MigrationId { get; }

    /// <summary>
    /// Returns a human-readable description of this migration.
    /// </summary>
    protected abstract string MigrationDescription { get; }

    /// <summary>
    /// Returns the DDL statement to execute for a single per-symbol table.
    /// The DDL should be idempotent (use IF NOT EXISTS / safe-add patterns).
    /// </summary>
    /// <param name="tableName">The per-symbol table name (e.g. "D_RELIANCE").</param>
    /// <param name="schema">The table schema (typically "dbo").</param>
    /// <returns>The DDL string, or <c>null</c> to skip this table.</returns>
    protected abstract Task<string?> GetDdlAsync(
        string tableName,
        string schema,
        CancellationToken ct);

    /// <summary>
    /// Optional post-DDL statement to run after each per-table DDL.
    /// </summary>
    protected virtual Task<string?> GetDdlPostStatementAsync(
        string tableName,
        string schema,
        CancellationToken ct) => Task.FromResult<string?>(null);

    /// <summary>
    /// Called once before the first symbol batch.  Return false to abort.
    /// </summary>
    protected virtual Task<bool> OnBeforeSymbolBatchAsync(CancellationToken ct)
        => Task.FromResult(true);

    /// <summary>
    /// Called once after all symbols have been processed.
    /// </summary>
    protected virtual Task OnAfterSymbolBatchAsync(
        int totalSymbols,
        int totalDdlsExecuted,
        IReadOnlyList<string> errors,
        CancellationToken ct) => Task.CompletedTask;

    /// <summary>
    /// Determines which SQL table name suffixes to process.
    /// Override to filter to a subset (e.g. only D_ tables, only active symbols).
    /// Default: all three suffixes (D_, W_, M_, S_).
    /// </summary>
    protected virtual string[] GetTablePrefixes() => ["D_", "W_", "M_"];

    /// <summary>
    /// Executes the batched migration.
    /// </summary>
    /// <param name="explicitSymbols">
    /// Optional list of symbols to process.  When null, processes all active symbols.
    /// </param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A result describing what was done.</returns>
    public async Task<BatchedMigrationResult> ExecuteAsync(
        IReadOnlyList<string>? explicitSymbols = null,
        CancellationToken ct = default)
    {
        _logger.LogInformation(
            "Batched migration [{MigrationId}] starting: {Description}.",
            MigrationId, MigrationDescription);

        // ── Check dual-read window ────────────────────────────────────────
        var dualReadActive = await _dualReadWindow.IsDualReadActiveAsync(ct);
        if (dualReadActive)
        {
            _logger.LogWarning(
                "Dual-read window is active for migration [{MigrationId}]. " +
                "Application code should accept both old and new schemas " +
                "during this window (REQ-MIGRATION-005).",
                MigrationId);
        }
        else
        {
            _logger.LogInformation(
                "Dual-read window is inactive for migration [{MigrationId}]. " +
                "New schema is authoritative.",
                MigrationId);
        }

        // ── Pre-flight: OnBeforeSymbolBatchAsync ──────────────────────────
        var proceed = await OnBeforeSymbolBatchAsync(ct);
        if (!proceed)
        {
            _logger.LogWarning(
                "Migration [{MigrationId}] aborted by OnBeforeSymbolBatchAsync.",
                MigrationId);
            return new BatchedMigrationResult(MigrationId, 0, 0, 0, [], "aborted_by_preflight");
        }

        // ── Resolve symbols ───────────────────────────────────────────────
        var symbols = explicitSymbols is { Count: > 0 }
            ? explicitSymbols.ToList()
            : await GetAllActiveSymbolsAsync(ct);

        if (symbols.Count == 0)
        {
            _logger.LogWarning(
                "Migration [{MigrationId}]: no symbols to process.",
                MigrationId);
            return new BatchedMigrationResult(MigrationId, 0, 0, 0, [], "no_symbols");
        }

        _logger.LogInformation(
            "Migration [{MigrationId}]: processing {Count} symbols.",
            MigrationId, symbols.Count);

        // ── Connect ───────────────────────────────────────────────────────
        await using var conn = new SqlConnection(_sqlConnectionString);
        await conn.OpenAsync(ct);

        var tablePrefixes = GetTablePrefixes();
        var totalDdls = 0;
        var totalSkipped = 0;
        var errors = new List<string>();

        foreach (var symbol in symbols)
        {
            if (ct.IsCancellationRequested)
            {
                _logger.LogWarning(
                    "Migration [{MigrationId}] cancelled after {Processed}/{Total} symbols.",
                    MigrationId, symbols.IndexOf(symbol), symbols.Count);
                break;
            }

            var suffix = await _tableMapping.GetSuffixAsync(symbol, ct);
            if (suffix is null)
            {
                _logger.LogWarning(
                    "Migration [{MigrationId}]: symbol {Symbol} has no suffix — skipping.",
                    MigrationId, symbol);
                continue;
            }

            foreach (var prefix in tablePrefixes)
            {
                var tableName = $"{prefix}{suffix}";
                const string schema = "dbo";

                try
                {
                    var ddl = await GetDdlAsync(tableName, schema, ct);

                    if (string.IsNullOrWhiteSpace(ddl))
                    {
                        _logger.LogDebug(
                            "Migration [{MigrationId}]: no DDL for {Table} (skipped).",
                            MigrationId, tableName);
                        totalSkipped++;
                        continue;
                    }

                    await using var cmd = new SqlCommand(ddl, conn);
                    cmd.CommandTimeout = 120; // Allow long-running DDL.
                    await cmd.ExecuteNonQueryAsync(ct);

                    // ── Post-DDL statement ────────────────────────────────
                    var postDdl = await GetDdlPostStatementAsync(tableName, schema, ct);
                    if (!string.IsNullOrWhiteSpace(postDdl))
                    {
                        await using var postCmd = new SqlCommand(postDdl, conn);
                        postCmd.CommandTimeout = 120;
                        await postCmd.ExecuteNonQueryAsync(ct);
                    }

                    totalDdls++;
                    _logger.LogDebug(
                        "Migration [{MigrationId}]: applied DDL to {Table}.",
                        MigrationId, tableName);
                }
                catch (Exception ex)
                {
                    var error = $"{symbol}/{tableName}: {ex.Message}";
                    errors.Add(error);
                    _logger.LogError(ex,
                        "Migration [{MigrationId}]: DDL failed for {Table}.",
                        MigrationId, tableName);
                }
            }
        }

        // ── Post-batch: OnAfterSymbolBatchAsync ───────────────────────────
        await OnAfterSymbolBatchAsync(symbols.Count, totalDdls, errors, ct);

        var outcome = errors.Count == 0 ? "completed" : "completed_with_errors";

        _logger.LogInformation(
            "Migration [{MigrationId}] finished: {Outcome}. " +
            "{Ddls} DDL statements executed across {Symbols} symbols " +
            "({Skipped} skipped, {Errors} errors).",
            MigrationId, outcome, totalDdls, symbols.Count, totalSkipped, errors.Count);

        return new BatchedMigrationResult(
            MigrationId, symbols.Count, totalDdls, totalSkipped, errors, outcome);
    }

    private async Task<List<string>> GetAllActiveSymbolsAsync(CancellationToken ct)
    {
        var symbols = await _symbolMasterRepo.GetAllAsync(archived: false, ct);
        return symbols.Select(s => s.Symbol).OrderBy(s => s).ToList();
    }
}

/// <summary>
/// Result of a batched migration run.
/// </summary>
public sealed record BatchedMigrationResult(
    string MigrationId,
    int SymbolsProcessed,
    int DdlStatementsExecuted,
    int DdlStatementsSkipped,
    IReadOnlyList<string> Errors,
    string Outcome);
