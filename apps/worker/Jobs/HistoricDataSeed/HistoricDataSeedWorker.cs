using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Storage.Universe;

namespace SignalStack.Worker.Jobs.HistoricDataSeed;

/// <summary>
/// BackgroundService that polls the <c>job_runs</c> MongoDB collection for
/// pending HistoricDataSeed (HDS) jobs and processes them.
///
/// REQ-HIST-009: per-symbol table materialisation and data seeding.
/// REQ-HIST-009a: idempotent — second run for the same symbol is a no-op;
/// resumable from last successful date.
///
/// The HDS worker is triggered by <c>job_runs</c> records with
/// <c>job_type: "HDS"</c> and <c>outcome: null</c> (pending). These records
/// are typically created by:
/// - Universe Sync commit (P3-T6) for net-new symbols added via CSV upload
/// - Admin reseed action (P3-T6) for out-of-sync symbols
///
/// On completion, the job_runs record is updated with the outcome summary.
/// </summary>
internal sealed class HistoricDataSeedWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<HistoricDataSeedOptions> _options;
    private readonly ILogger<HistoricDataSeedWorker> _logger;

    public HistoricDataSeedWorker(
        IServiceProvider serviceProvider,
        IOptions<HistoricDataSeedOptions> options,
        ILogger<HistoricDataSeedWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds);

        _logger.LogInformation(
            "HistoricDataSeedWorker started (poll interval: {Interval}s).",
            _options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessPendingJobsAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "HistoricDataSeedWorker error while processing jobs.");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Finds and processes the next pending HDS job from the <c>job_runs</c> collection.
    /// Processes exactly one job per poll cycle to keep each task atomic.
    /// </summary>
    private async Task ProcessPendingJobsAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

        var database = services.GetRequiredService<IMongoDatabase>();
        var symbolMasterRepo = services.GetRequiredService<ISymbolMasterRepository>();

        // Find the oldest pending HDS job.
        var jobRuns = database.GetCollection<BsonDocument>("job_runs");
        var pendingFilter = Builders<BsonDocument>.Filter.Eq("job_type", "HDS")
                          & Builders<BsonDocument>.Filter.Eq("outcome", BsonNull.Value);

        var pendingJob = await jobRuns
            .Find(pendingFilter)
            .Sort(Builders<BsonDocument>.Sort.Ascending("started_at"))
            .FirstOrDefaultAsync(ct);

        if (pendingJob is null)
        {
            // No pending HDS jobs.
            return;
        }

        var jobId = pendingJob["_id"].AsObjectId;
        _logger.LogInformation(
            "Processing pending HDS job {JobId} (triggered_by: {TriggeredBy}).",
            jobId, pendingJob.GetValue("triggered_by", "unknown"));

        // ── Mark job as in-progress ────────────────────────────────────────
        var inProgressUpdate = Builders<BsonDocument>.Filter.Eq("_id", jobId);
        var inProgressFields = Builders<BsonDocument>.Update
            .Set("outcome", "in_progress")
            .Set("started_at", DateTime.UtcNow);
        await jobRuns.UpdateOneAsync(inProgressUpdate, inProgressFields, cancellationToken: ct);

        // ── Determine which symbols to seed ────────────────────────────────
        // The job_runs record may contain explicit symbols, or an upload_id
        // referencing a universe upload containing symbols to seed.
        List<string> symbolsToSeed;

        if (pendingJob.TryGetValue("symbols", out var symbolsValue) && symbolsValue.IsBsonArray)
        {
            // Explicit list of symbols.
            symbolsToSeed = symbolsValue.AsBsonArray
                .Select(s => s.AsString)
                .Where(s => !string.IsNullOrWhiteSpace(s))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }
        else if (pendingJob.TryGetValue("symbol", out var singleSymbol) && singleSymbol.IsString)
        {
            // Single symbol.
            symbolsToSeed = [singleSymbol.AsString];
        }
        else if (pendingJob.TryGetValue("upload_id", out var uploadId) && uploadId.IsObjectId)
        {
            // Universe upload reference — fetch all non-archived symbols from
            // the symbol master that were added by this upload.
            symbolsToSeed = await GetSymbolsFromUploadAsync(database, uploadId.AsObjectId, ct);
        }
        else
        {
            // No symbols specified — seed all non-archived symbols.
            _logger.LogInformation(
                "HDS job {JobId} has no symbol list or upload_id. " +
                "Falling back to seeding all active symbols.",
                jobId);

            var allSymbols = await symbolMasterRepo.GetAllAsync(archived: false, ct);
            symbolsToSeed = allSymbols
                .Select(s => s.Symbol)
                .OrderBy(s => s)
                .ToList();
        }

        if (symbolsToSeed.Count == 0)
        {
            _logger.LogWarning("HDS job {JobId} resolved to zero symbols. Marking as complete.", jobId);

            var completeUpdate = Builders<BsonDocument>.Update
                .Set("outcome", "completed")
                .Set("ended_at", DateTime.UtcNow)
                .Set("symbols_processed", 0)
                .Set("symbols_total", 0)
                .Set("result", "no_symbols");
            await jobRuns.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", jobId),
                completeUpdate,
                cancellationToken: ct);
            return;
        }

        // ── Run the seed ──────────────────────────────────────────────────
        var hdsService = services.GetRequiredService<HistoricDataSeedService>();
        var historicalYearsBack = _options.Value.HistoricalYearsBack;

        var processedCount = 0;
        var successCount = 0;
        var failureCount = 0;
        var skippedCount = 0;
        var totalDaily = 0;
        var totalWeekly = 0;
        var totalMonthly = 0;
        var errors = new List<string>();

        foreach (var symbol in symbolsToSeed)
        {
            if (ct.IsCancellationRequested) break;

            _logger.LogInformation(
                "HDS job {JobId}: seeding symbol {Symbol} ({Processed}/{Total}).",
                jobId, symbol, processedCount + 1, symbolsToSeed.Count);

            try
            {
                var result = await hdsService.SeedSymbolAsync(
                    symbol, historicalYearsBack, ct);

                processedCount++;

                switch (result.Outcome)
                {
                    case SeedOutcome.Success:
                        successCount++;
                        totalDaily += result.DailyInserted;
                        totalWeekly += result.WeeklyComputed;
                        totalMonthly += result.MonthlyComputed;
                        break;
                    case SeedOutcome.AlreadyUpToDate:
                    case SeedOutcome.SkippedNoSuffix:
                    case SeedOutcome.NoDataReturned:
                        skippedCount++;
                        break;
                    default:
                        failureCount++;
                        errors.Add($"{symbol}: {result.Outcome}");
                        break;
                }

                // Update progress on job_runs after each symbol.
                var progressUpdate = Builders<BsonDocument>.Update
                    .Set("symbols_processed", processedCount)
                    .Set("symbols_total", symbolsToSeed.Count)
                    .Set("last_symbol", symbol);
                await jobRuns.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", jobId),
                    progressUpdate,
                    cancellationToken: ct);
            }
            catch (Exception ex)
            {
                processedCount++;
                failureCount++;
                errors.Add($"{symbol}: {ex.Message}");
                _logger.LogError(ex,
                    "HDS job {JobId}: failed to seed symbol {Symbol}.",
                    jobId, symbol);
            }
        }

        // ── Mark job as complete ──────────────────────────────────────────
        var outcome = failureCount == 0 ? "completed" : "completed_with_errors";
        var resultDoc = new BsonDocument
        {
            ["symbols_total"] = symbolsToSeed.Count,
            ["symbols_processed"] = processedCount,
            ["success_count"] = successCount,
            ["failure_count"] = failureCount,
            ["skipped_count"] = skippedCount,
            ["daily_records_inserted"] = totalDaily,
            ["weekly_records_computed"] = totalWeekly,
            ["monthly_records_computed"] = totalMonthly,
        };

        if (errors.Count > 0)
        {
            resultDoc["errors"] = new BsonArray(errors.Select(e => new BsonString(e)));
        }

        var finalUpdate = Builders<BsonDocument>.Update
            .Set("outcome", outcome)
            .Set("ended_at", DateTime.UtcNow)
            .Set("result", resultDoc);

        await jobRuns.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", jobId),
            finalUpdate,
            cancellationToken: ct);

        _logger.LogInformation(
            "HDS job {JobId} completed: {Outcome}. " +
            "{Success}/{Total} symbols seeded, " +
            "{Daily} daily / {Weekly} weekly / {Monthly} monthly records.",
            jobId, outcome,
            successCount, symbolsToSeed.Count,
            totalDaily, totalWeekly, totalMonthly);
    }

    /// <summary>
    /// Retrieves symbols from a universe upload record. The upload record
    /// contains a list of symbols that were added/modified in the sync.
    /// </summary>
    private static async Task<List<string>> GetSymbolsFromUploadAsync(
        IMongoDatabase database,
        ObjectId uploadId,
        CancellationToken ct)
    {
        var uploads = database.GetCollection<BsonDocument>("universe_uploads");
        var upload = await uploads.Find(
            Builders<BsonDocument>.Filter.Eq("_id", uploadId))
            .FirstOrDefaultAsync(ct);

        if (upload is null)
        {
            return [];
        }

        // The upload record contains a list of symbol entries.
        // Extract all unique symbols from the changeset.
        var symbols = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (upload.TryGetValue("added_symbols", out var added) && added.IsBsonArray)
        {
            foreach (var s in added.AsBsonArray)
            {
                if (s.IsString && !string.IsNullOrWhiteSpace(s.AsString))
                    symbols.Add(s.AsString);
            }
        }

        if (upload.TryGetValue("modified_symbols", out var modified) && modified.IsBsonArray)
        {
            foreach (var s in modified.AsBsonArray)
            {
                if (s.IsString && !string.IsNullOrWhiteSpace(s.AsString))
                    symbols.Add(s.AsString);
            }
        }

        return symbols.ToList();
    }
}
