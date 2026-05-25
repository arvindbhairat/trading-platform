using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Domain.Admin;
using SignalStack.Storage.Admin;
using SignalStack.Worker.Integrations.Fyers;

namespace SignalStack.Worker.Jobs.DataSync;

/// <summary>
/// BackgroundService that performs the daily DataSync (DS) job.
///
/// On each poll cycle, the worker checks <c>job_runs</c> for pending DS jobs.
/// If none exist, it auto-detects whether a DS run is needed by checking:
/// (a) whether the current date is a past or present trading day, and
/// (b) whether a DS success marker already exists for that session.
///
/// If conditions are met, it creates a <c>job_runs</c> entry with
/// {@code job_type: "DS"} and processes it.
///
/// REQ-MARKET-003:  post-market DataSync refreshes SQL Server historical data.
/// REQ-MARKET-005:  fails on provider/auth failure with structured Error log.
/// REQ-MARKET-005a: re-checks admin token at every session boundary.
/// REQ-MARKET-006:  skips symbols with unavailable daily data.
/// REQ-MARKET-007:  writes explicit success marker for the trading session.
/// REQ-MARKET-009:  10-session auto-recovery for missed sync days.
/// </summary>
internal sealed class DataSyncWorker : BackgroundService
{
    private const string DsJobType = "DS";
    private const string OutcomeCompleted = "completed";
    private const string OutcomePartialTokenExpired = "partial_completion_token_expired";
    private const string OutcomeFailed = "failed";

    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<DataSyncOptions> _options;
    private readonly ILogger<DataSyncWorker> _logger;

    public DataSyncWorker(
        IServiceProvider serviceProvider,
        IOptions<DataSyncOptions> options,
        ILogger<DataSyncWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds);

        _logger.LogInformation(
            "DataSyncWorker started (poll interval: {Interval}s).",
            _options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessDataSyncCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "DataSyncWorker error during processing cycle.");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Main processing cycle: checks for pending DS jobs or auto-detects
    /// whether a run is needed, then executes.
    /// </summary>
    private async Task ProcessDataSyncCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

        var database = services.GetRequiredService<IMongoDatabase>();
        var calendarRepo = services.GetRequiredService<ITradingCalendarRepository>();
        var dataSyncService = services.GetRequiredService<DataSyncService>();
        var jobRuns = database.GetCollection<BsonDocument>("job_runs");

        // REQ-MARKET-016(b): shared-ingestion token loss → DS suspends.
        var healthService = _serviceProvider.GetRequiredService<SharedTokenHealthService>();
        if (healthService.IsDegraded)
        {
            _logger.LogWarning(
                "DataSync: shared-ingestion token is degraded. " +
                "Suspending sync cycle until token is restored (REQ-MARKET-016).");
            return;
        }

        // ── Step 1: Check for an existing pending DS job in job_runs ────────
        var pendingFilter = Builders<BsonDocument>.Filter.Eq("job_type", DsJobType)
                          & Builders<BsonDocument>.Filter.Eq("outcome", BsonNull.Value);
        var pendingJob = await jobRuns
            .Find(pendingFilter)
            .Sort(Builders<BsonDocument>.Sort.Ascending("started_at"))
            .FirstOrDefaultAsync(ct);

        if (pendingJob is not null)
        {
            // Pending DS job found — process it.
            var jobId = pendingJob["_id"].AsObjectId;
            _logger.LogInformation(
                "Processing pending DS job {JobId}.", jobId);

            await ExecuteDsJobAsync(jobRuns, jobId, dataSyncService, ct);
            return;
        }

        // ── Step 2: Auto-detect whether a DS run is needed ─────────────────
        // Check if today (or recent past trading days) needs a DS run.
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var recoveryCount = _options.Value.RecoverySessionCount;

        // Get trading sessions in the last N days.
        var lookBackDays = recoveryCount * 7; // generous buffer
        var fromDate = today.AddDays(-lookBackDays);

        var recentSessions = await calendarRepo.GetSessionsAsync(
            fromDate: fromDate.ToString("yyyy-MM-dd"),
            toDate: today.ToString("yyyy-MM-dd"),
            ct: ct);

        // Filter to sessions in the past or today.
        var pendingSessionDates = recentSessions
            .Select(s =>
            {
                if (DateOnly.TryParse(s.SessionDate, out var d))
                    return (d, s.SessionStartTime, s.SessionEndTime);
                return ((DateOnly, string?, string?))default;
            })
            .Where(t => t.Item1 != default && t.Item1 <= today)
            .Select(t => (Date: t.Item1, StartTime: t.Item2, EndTime: t.Item3))
            .OrderByDescending(t => t.Date)
            .Take(recoveryCount)
            .ToList();

        if (pendingSessionDates.Count == 0)
        {
            // No sessions to process.
            return;
        }

        // Check which sessions already have a DS success marker.
        // A DS success marker is a job_runs record with job_type "DS" and
        // outcome "completed" for the matching session date.
        var sessionDateStrings = pendingSessionDates
            .Select(s => s.Date.ToString("yyyy-MM-dd"))
            .ToList();

        var existingMarkerFilter = Builders<BsonDocument>.Filter.Eq("job_type", DsJobType)
                                 & Builders<BsonDocument>.Filter.Eq("outcome", OutcomeCompleted)
                                 & Builders<BsonDocument>.Filter.In("session_date", new BsonArray(sessionDateStrings));

        var existingMarkers = await jobRuns
            .Find(existingMarkerFilter)
            .Project(Builders<BsonDocument>.Projection.Include("session_date"))
            .ToListAsync(ct);

        var completedSessions = new HashSet<string>(
            existingMarkers
                .Select(m => m.GetValue("session_date", BsonNull.Value)?.AsString)
                .Where(s => s is not null)!,
            StringComparer.OrdinalIgnoreCase);

        // Find the most recent session that doesn't have a success marker.
        var missingSessions = pendingSessionDates
            .Where(s => !completedSessions.Contains(s.Date.ToString("yyyy-MM-dd")))
            .Select(s => s.Date)
            .OrderByDescending(d => d)
            .Take(recoveryCount)
            .ToList();

        if (missingSessions.Count == 0)
        {
            // All recent sessions are already synced.
            return;
        }

        // ── Step 3: Create a DS job_runs entry and process it ──────────────
        var now = DateTime.UtcNow;
        var oldestMissing = missingSessions[^1]; // oldest first in recovery
        var latestMissing = missingSessions[0];  // most recent

        var dsJobDoc = new BsonDocument
        {
            ["job_type"] = DsJobType,
            ["trigger_source"] = "auto_detected",
            ["scheduled_run_time"] = now,
            ["started_at"] = now,
            ["session_date_range_start"] = oldestMissing.ToString("yyyy-MM-dd"),
            ["session_date_range_end"] = latestMissing.ToString("yyyy-MM-dd"),
            ["session_count"] = missingSessions.Count,
            ["outcome"] = BsonNull.Value,
        };

        await jobRuns.InsertOneAsync(dsJobDoc, cancellationToken: ct);
        var newJobId = dsJobDoc["_id"].AsObjectId;

        _logger.LogInformation(
            "DataSync: auto-detected {Count} unsynced sessions from {First} to {Last}. " +
            "Created DS job {JobId}.",
            missingSessions.Count, oldestMissing, latestMissing, newJobId);

        await ExecuteDsJobAsync(jobRuns, newJobId, dataSyncService, ct);
    }

    /// <summary>
    /// Executes a DataSync job and records the outcome in <c>job_runs</c>.
    /// </summary>
    private async Task ExecuteDsJobAsync(
        IMongoCollection<BsonDocument> jobRuns,
        ObjectId jobId,
        DataSyncService dataSyncService,
        CancellationToken ct)
    {
        // Mark as in-progress.
        var inProgressUpdate = Builders<BsonDocument>.Filter.Eq("_id", jobId);
        var inProgressFields = Builders<BsonDocument>.Update
            .Set("outcome", "in_progress")
            .Set("started_at", DateTime.UtcNow);
        await jobRuns.UpdateOneAsync(inProgressUpdate, inProgressFields, cancellationToken: ct);

        DataSyncResult result;
        try
        {
            result = await dataSyncService.RunSyncAsync(
                _options.Value.RecoverySessionCount, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Worker shutting down — mark as cancelled.
            var cancelUpdate = Builders<BsonDocument>.Update
                .Set("outcome", "cancelled")
                .Set("ended_at", DateTime.UtcNow);
            await jobRuns.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", jobId),
                cancelUpdate,
                cancellationToken: CancellationToken.None);
            return;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "DataSync job {JobId} failed with unhandled exception.", jobId);

            var failUpdate = Builders<BsonDocument>.Update
                .Set("outcome", OutcomeFailed)
                .Set("ended_at", DateTime.UtcNow)
                .Set("error", ex.Message);
            await jobRuns.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", jobId),
                failUpdate,
                cancellationToken: CancellationToken.None);
            return;
        }

        // ── Determine outcome and write result ─────────────────────────────
        // REQ-MARKET-007: success marker is a job_runs record with
        // job_type "DS" and outcome "completed".
        // For partial_completion_token_expired, we write partial marker.
        // For other failures, we write failed marker.

        var outcomeField = result.Outcome switch
        {
            "completed" => OutcomeCompleted,
            "partial_completion_token_expired" => OutcomePartialTokenExpired,
            "no_sessions_needed" => OutcomeCompleted, // nothing to do is a successful no-op
            _ => OutcomeFailed
        };

        var resultDoc = new BsonDocument
        {
            ["sessions_processed"] = result.SessionsProcessed,
            ["sessions_total"] = result.SessionsTotal,
            ["total_symbol_sessions_processed"] = result.TotalSymbolSessionsProcessed,
            ["symbols_skipped_data_unavailable"] = result.SymbolsSkippedDataUnavailable,
            ["symbols_skipped_validation_failed"] = result.SymbolsSkippedValidationFailed,
        };

        if (result.LastSuccessfulSession.HasValue)
        {
            resultDoc["last_successful_session"] = result.LastSuccessfulSession.Value.ToString("yyyy-MM-dd");
        }

        // Write the success marker. For a completed run, the first processed
        // session date is used as the primary session_date marker.
        // REQ-MARKET-007: EOD Signal Runner checks for this marker.
        var sessionDate = result.LastSuccessfulSession?.ToString("yyyy-MM-dd");

        var finalUpdate = Builders<BsonDocument>.Update
            .Set("outcome", outcomeField)
            .Set("ended_at", DateTime.UtcNow)
            .Set("result", resultDoc);

        if (sessionDate is not null)
        {
            finalUpdate = finalUpdate.Set("session_date", sessionDate);
        }

        if (result.Outcome == "partial_completion_token_expired")
        {
            finalUpdate = finalUpdate.Set("completion_hint",
                "Token expired mid-run. Sessions up to and including " +
                $"{result.LastSuccessfulSession?.ToString("yyyy-MM-dd") ?? "(none)"} " +
                "were completed before expiry.");
        }

        await jobRuns.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", jobId),
            finalUpdate,
            cancellationToken: ct);

        // ── Write per-session success markers (REQ-MARKET-007) ───────────────
        // For a completed run, each processed session gets its own lightweight
        // marker document so that EODSR (and the auto-detect logic) can verify
        // that DataSync has completed for any individual session date.
        // Markers are idempotent: ReplaceOneAsync with upsert:true ensures
        // exactly one marker per session regardless of re-runs.
        if (outcomeField == OutcomeCompleted
            && result.CompletedSymbolsPerSession is { Count: > 0 }
            && result.CompletedSymbolsPerSession.Count > 1)
        {
            foreach (var (session, _) in result.CompletedSymbolsPerSession)
            {
                var sessionStr = session.ToString("yyyy-MM-dd");

                var markerFilter = Builders<BsonDocument>.Filter.Eq("job_type", DsJobType)
                                 & Builders<BsonDocument>.Filter.Eq("marker_type", "eod")
                                 & Builders<BsonDocument>.Filter.Eq("session_date", sessionStr);

                var markerDoc = new BsonDocument
                {
                    ["job_type"] = DsJobType,
                    ["marker_type"] = "eod",
                    ["outcome"] = OutcomeCompleted,
                    ["session_date"] = sessionStr,
                    ["parent_job_id"] = jobId,
                    ["created_at"] = DateTime.UtcNow,
                };

                await jobRuns.ReplaceOneAsync(
                    markerFilter,
                    markerDoc,
                    new ReplaceOptions { IsUpsert = true },
                    cancellationToken: ct);
            }

            _logger.LogInformation(
                "DataSync: wrote {Count} per-session EOD success markers " +
                "for parent job {JobId}.",
                result.CompletedSymbolsPerSession.Count, jobId);
        }

        _logger.LogInformation(
            "DataSync job {JobId} completed: {Outcome}. " +
            "{Processed}/{Total} sessions, {SymbolSessions} total symbol-sessions.",
            jobId, outcomeField,
            result.SessionsProcessed, result.SessionsTotal,
            result.TotalSymbolSessionsProcessed);
    }
}
