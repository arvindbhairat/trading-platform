using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Domain.Admin;
using SignalStack.Storage.Admin;
using SignalStack.Worker.Integrations.Fyers;
using SignalStack.Worker.Jobs.EodSuccessMarker;

namespace SignalStack.Worker.Jobs.EodSignalRunner;

/// <summary>
/// BackgroundService that performs the EOD Signal Runner (EODSR) job.
///
/// On each poll cycle, the worker checks <c>job_runs</c> for pending EODSR jobs.
/// If none exist, it auto-detects whether an EODSR run is needed by checking:
/// (a) whether DataSync has completed for the latest trading session, and
/// (b) whether EODSR has already run for the next trading day.
///
/// REQ-STRAT-013:  read SQL Server data and evaluate user-subscribed Signal types.
/// REQ-STRAT-013a: user-scoped execution with single-pass market data read.
/// REQ-STRAT-027:  atomic-run semantics — full success or failed_for_review.
/// </summary>
internal sealed class EodSignalRunnerWorker : BackgroundService
{
    private const string EodsrJobType = "EODSR";
    private const string OutcomeCompleted = "completed";
    private const string OutcomeFailedForReview = "failed_for_review";

    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<EodSignalRunnerOptions> _options;
    private readonly ILogger<EodSignalRunnerWorker> _logger;

    public EodSignalRunnerWorker(
        IServiceProvider serviceProvider,
        IOptions<EodSignalRunnerOptions> options,
        ILogger<EodSignalRunnerWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds);

        _logger.LogInformation(
            "EODSR worker started (poll interval: {Interval}s).",
            _options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessEodsrCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "EODSR worker error during processing cycle.");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Main processing cycle: checks for pending EODSR jobs or auto-detects
    /// whether a run is needed, then executes.
    ///
    /// REQ-MARKET-016(c): if shared-ingestion token is degraded, records
    /// <c>skipped_token_unavailable</c> and skips entry-signal evaluation.
    /// </summary>
    private async Task ProcessEodsrCycleAsync(CancellationToken ct)
    {
        using var scope = _serviceProvider.CreateScope();
        var services = scope.ServiceProvider;

        var database = services.GetRequiredService<IMongoDatabase>();
        var calendarRepo = services.GetRequiredService<ITradingCalendarRepository>();
        var eodMarkerReader = services.GetRequiredService<IEodMarkerReader>();
        var eodSrService = services.GetRequiredService<EodSignalRunnerService>();
        var jobRuns = database.GetCollection<BsonDocument>("job_runs");

        // REQ-MARKET-016(c): shared-ingestion token loss → EODSR suspends.
        var healthService = _serviceProvider.GetRequiredService<SharedTokenHealthService>();
        if (healthService.IsDegraded)
        {
            _logger.LogWarning(
                "EODSR: shared-ingestion token is degraded. " +
                "Recording skipped_token_unavailable (REQ-MARKET-016).");

            var skipNow = DateTime.UtcNow;
            var skippedDoc = new BsonDocument
            {
                ["job_type"] = EodsrJobType,
                ["trigger_source"] = "auto_detected",
                ["scheduled_run_time"] = skipNow,
                ["started_at"] = skipNow,
                ["ended_at"] = skipNow,
                ["outcome"] = "skipped_token_unavailable",
                ["completion_hint"] =
                    "Shared-ingestion admin FYERS token is unavailable. " +
                    "EODSR will not emit entry signals until the token is restored.",
            };

            await jobRuns.InsertOneAsync(skippedDoc, cancellationToken: ct);
            return;
        }

        // ── Step 1: Check for an existing pending EODSR job ──────────────────
        var pendingFilter = Builders<BsonDocument>.Filter.Eq("job_type", EodsrJobType)
                          & Builders<BsonDocument>.Filter.Eq("outcome", BsonNull.Value);
        var pendingJob = await jobRuns
            .Find(pendingFilter)
            .Sort(Builders<BsonDocument>.Sort.Ascending("started_at"))
            .FirstOrDefaultAsync(ct);

        if (pendingJob is not null)
        {
            var jobId = pendingJob["_id"].AsObjectId;
            var sessionDateStr = pendingJob.GetValue("session_date", BsonNull.Value)?.AsString;

            _logger.LogInformation(
                "Processing pending EODSR job {JobId} for session {Session}.",
                jobId, sessionDateStr ?? "(not set)");

            await ExecuteEodSrJobAsync(jobRuns, jobId, sessionDateStr, eodSrService, ct);
            return;
        }

        // ── Step 2: Auto-detect whether an EODSR run is needed ───────────────
        // Find the latest DS-completed session, then check if EODSR has already
        // run for the next trading day.
        var now = DateTime.UtcNow;
        var today = DateOnly.FromDateTime(now);

        // Look for DS success markers in recent sessions (last 14 days).
        var lookBackFrom = today.AddDays(-14);
        var recentSessions = await calendarRepo.GetSessionsAsync(
            fromDate: lookBackFrom.ToString("yyyy-MM-dd"),
            toDate: today.ToString("yyyy-MM-dd"),
            ct: ct);

        if (recentSessions.Count == 0)
        {
            _logger.LogDebug("EODSR: no recent trading sessions found. Skipping cycle.");
            return;
        }

        // Find the latest session that has a DS success marker.
        var sessionDateCandidates = recentSessions
            .Select(s =>
            {
                if (DateOnly.TryParse(s.SessionDate, out var d))
                    return d;
                return (DateOnly?)null;
            })
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .OrderByDescending(d => d)
            .ToList();

        foreach (var sessionDate in sessionDateCandidates)
        {
            // Check if DS has completed for this session.
            var dsCompleted = await eodMarkerReader.HasCompletedAsync(sessionDate, ct);
            if (!dsCompleted)
                continue;

            // Check if EODSR has already completed for the next trading day.
            var nextDay = await ResolveNextDayForSessionAsync(
                sessionDate, calendarRepo, ct);

            if (nextDay is null)
                continue;

            var eodsrCompleted = await HasEodSrCompletedForDateAsync(
                jobRuns, nextDay.Value, ct);

            if (!eodsrCompleted)
            {
                // Create a job_runs entry and process it.
                var eodsrJobDoc = new BsonDocument
                {
                    ["job_type"] = EodsrJobType,
                    ["trigger_source"] = "auto_detected",
                    ["scheduled_run_time"] = now,
                    ["started_at"] = now,
                    ["session_date"] = sessionDate.ToString("yyyy-MM-dd"),
                    ["next_trading_day"] = nextDay.Value.ToString("yyyy-MM-dd"),
                    ["outcome"] = BsonNull.Value,
                };

                await jobRuns.InsertOneAsync(eodsrJobDoc, cancellationToken: ct);
                var newJobId = eodsrJobDoc["_id"].AsObjectId;

                _logger.LogInformation(
                    "EODSR: auto-detected need for run. DS completed for session {DSession}, " +
                    "evaluating signals for {NextDay}. Created EODSR job {JobId}.",
                    sessionDate, nextDay.Value, newJobId);

                await ExecuteEodSrJobAsync(
                    jobRuns, newJobId, sessionDate.ToString("yyyy-MM-dd"), eodSrService, ct);
                return;
            }
        }

        _logger.LogDebug("EODSR: no pending or auto-detectable work. Skipping cycle.");
    }

    /// <summary>
    /// Executes an EODSR job and records the outcome in <c>job_runs</c>.
    /// REQ-STRAT-027: atomic-run — either full success or failed_for_review.
    /// </summary>
    private async Task ExecuteEodSrJobAsync(
        IMongoCollection<BsonDocument> jobRuns,
        ObjectId jobId,
        string? sessionDateStr,
        EodSignalRunnerService eodSrService,
        CancellationToken ct)
    {
        // Mark as in-progress.
        var inProgressUpdate = Builders<BsonDocument>.Filter.Eq("_id", jobId);
        var inProgressFields = Builders<BsonDocument>.Update
            .Set("outcome", "in_progress")
            .Set("started_at", DateTime.UtcNow);
        await jobRuns.UpdateOneAsync(inProgressUpdate, inProgressFields, cancellationToken: ct);

        EodSignalRunnerResult result;
        try
        {
            if (!DateOnly.TryParse(sessionDateStr, out var sessionDate))
            {
                _logger.LogError("EODSR job {JobId}: invalid session_date '{Session}'.", jobId, sessionDateStr);

                var failUpdate = Builders<BsonDocument>.Update
                    .Set("outcome", OutcomeFailedForReview)
                    .Set("ended_at", DateTime.UtcNow)
                    .Set("error", $"Invalid session_date: {sessionDateStr}");
                await jobRuns.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", jobId),
                    failUpdate,
                    cancellationToken: CancellationToken.None);
                return;
            }

            result = await eodSrService.RunAsync(sessionDate, ct);

            if (result.Outcome == "skipped_ds_not_ready")
            {
                // DS not ready — mark as skipped, will auto-retry on next poll.
                var skipUpdate = Builders<BsonDocument>.Update
                    .Set("outcome", "skipped")
                    .Set("ended_at", DateTime.UtcNow)
                    .Set("completion_hint", "DataSync has not completed for the target session. " +
                        "The EODSR job will be re-evaluated on the next poll cycle.");
                await jobRuns.UpdateOneAsync(
                    Builders<BsonDocument>.Filter.Eq("_id", jobId),
                    skipUpdate,
                    cancellationToken: CancellationToken.None);
                return;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
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
            _logger.LogError(ex, "EODSR job {JobId} failed with unhandled exception.", jobId);

            var failUpdate = Builders<BsonDocument>.Update
                .Set("outcome", OutcomeFailedForReview)
                .Set("ended_at", DateTime.UtcNow)
                .Set("error", ex.Message);
            await jobRuns.UpdateOneAsync(
                Builders<BsonDocument>.Filter.Eq("_id", jobId),
                failUpdate,
                cancellationToken: CancellationToken.None);
            return;
        }

        // ── Write outcome ──────────────────────────────────────────────────
        var outcomeField = result.Outcome switch
        {
            "completed" => OutcomeCompleted,
            "failed_for_review" => OutcomeFailedForReview,
            _ => "failed"
        };

        var resultDoc = new BsonDocument
        {
            ["entry_signal_count"] = result.EntrySignalCount,
            ["symbols_evaluated"] = result.SymbolsEvaluated,
        };

        if (result.NextTradingDay.HasValue)
        {
            resultDoc["next_trading_day"] = result.NextTradingDay.Value.ToString("yyyy-MM-dd");
        }

        var finalUpdate = Builders<BsonDocument>.Update
            .Set("outcome", outcomeField)
            .Set("ended_at", DateTime.UtcNow)
            .Set("result", resultDoc);

        // If failed_for_review, add completion hint for admin visibility.
        if (outcomeField == OutcomeFailedForReview)
        {
            finalUpdate = finalUpdate.Set("completion_hint",
                "EODSR run failed. No entry signals were emitted for this session " +
                "(REQ-STRAT-027 atomic-run semantics). Manual retry via admin UI required.");
        }

        await jobRuns.UpdateOneAsync(
            Builders<BsonDocument>.Filter.Eq("_id", jobId),
            finalUpdate,
            cancellationToken: ct);

        _logger.LogInformation(
            "EODSR job {JobId} completed: {Outcome}. " +
            "{SignalCount} entry signals, {SymbolCount} symbols evaluated.",
            jobId, outcomeField,
            result.EntrySignalCount, result.SymbolsEvaluated);
    }

    /// <summary>
    /// Checks whether EODSR has already completed for a given trading date.
    /// </summary>
    private static async Task<bool> HasEodSrCompletedForDateAsync(
        IMongoCollection<BsonDocument> jobRuns,
        DateOnly date,
        CancellationToken ct)
    {
        var dateStr = date.ToString("yyyy-MM-dd");

        var filter = Builders<BsonDocument>.Filter.Eq("job_type", EodsrJobType)
                   & Builders<BsonDocument>.Filter.Eq("outcome", OutcomeCompleted)
                   & Builders<BsonDocument>.Filter.Eq("session_date", dateStr);

        var count = await jobRuns.CountDocumentsAsync(filter, cancellationToken: ct);
        return count > 0;
    }

    /// <summary>
    /// Resolves the next trading day after a given session.
    /// </summary>
    private static async Task<DateOnly?> ResolveNextDayForSessionAsync(
        DateOnly sessionDate,
        ITradingCalendarRepository calendarRepo,
        CancellationToken ct)
    {
        var fromDate = sessionDate.AddDays(1);
        var toDate = sessionDate.AddDays(14);

        var sessions = await calendarRepo.GetSessionsAsync(
            fromDate: fromDate.ToString("yyyy-MM-dd"),
            toDate: toDate.ToString("yyyy-MM-dd"),
            ct: ct);

        return sessions
            .Select(s =>
            {
                if (DateOnly.TryParse(s.SessionDate, out var d))
                    return d;
                return (DateOnly?)null;
            })
            .Where(d => d.HasValue && d.Value >= fromDate)
            .OrderBy(d => d!.Value)
            .FirstOrDefault();
    }
}
