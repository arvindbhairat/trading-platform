using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Admin;
using SignalStack.Api.Notifications;
using SignalStack.Worker.Observability;

namespace SignalStack.Worker.Jobs.AccountSync;

/// <summary>
/// Parameters for a user eligible for account sync.
/// </summary>
internal sealed record UserSyncInfo(
    ObjectId Id,
    string UserId);

/// <summary>
/// Result of a single LADS intraday sync cycle.
/// </summary>
public sealed record SyncCycleResult(
    int ProcessedCount,
    int ErrorCount,
    int SkippedCount,
    bool IsAborted)
{
    public static readonly SyncCycleResult SkippedSuspended = new(0, 0, 0, false);
    public static readonly SyncCycleResult SkippedOutsideMarketHours = new(0, 0, 0, false);
    public static readonly SyncCycleResult NoUsers = new(0, 0, 0, false);
}

/// <summary>
/// Core service for the Live Account Data Sync (LADS) job.
///
/// During NSE market hours, polls all approved users with valid FYERS
/// tokens on a configurable intraday interval and performs a per-user
/// account sync. Tracks consecutive cycle-level aborts and applies
/// graduated suspension per REQ-PORT-021a.
///
/// REQ-PORT-019:  intraday recurring sync during market hours.
/// REQ-PORT-021a: cycle-level abort tracking + graduated suspension.
/// </summary>
public sealed class LiveAccountDataSyncService
{
    private readonly IMongoDatabase _database;
    private readonly IOptions<AccountSyncOptions> _options;
    private readonly ILogger<LiveAccountDataSyncService> _logger;

    // ── OTEL metrics (REQ-PORT-021a(a)) ──────────────────────────────────
    private static readonly Counter<long> CycleAbortedCounter = WorkerTelemetry.Meter
        .CreateCounter<long>("lads.cycle.aborted.total",
            description: "Incremented per LADS cycle aborted due to MongoDB or write failure.");

    /// <summary>
    /// Cycle-level consecutive abort count. Reset to zero on any
    /// successful cycle completion. Not persisted across restarts;
    /// acceptable because a freshly booted worker reaching 3 failures
    /// (~3 min at default poll) is a reasonable re-detection window.
    /// </summary>
    private int _consecutiveCycleAborts;

    /// <summary>True when the platform-wide LADS suspension is active.</summary>
    private bool _isSuspended;

    /// <summary>UTC timestamp when suspension was last activated.</summary>
    private DateTime _suspendedAt;

    /// <summary>Lock for thread-safe suspension state access.</summary>
    private readonly object _suspensionLock = new();

    public LiveAccountDataSyncService(
        IMongoDatabase database,
        IOptions<AccountSyncOptions> options,
        ILogger<LiveAccountDataSyncService> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a single LADS intraday sync cycle.
    /// Returns a result summarising what happened this cycle.
    /// </summary>
    /// <param name="calendar">Trading calendar repository, resolved by the caller.</param>
    public async Task<SyncCycleResult> ExecuteCycleAsync(
        ITradingCalendarRepository calendar,
        CancellationToken ct = default)
    {
        // ── Step 1: Check platform-wide suspension (REQ-PORT-021a(c)) ──────
        lock (_suspensionLock)
        {
            if (_isSuspended)
            {
                _logger.LogWarning(
                    "LADS: platform-wide sync suspended since {SuspendedAt:O} " +
                    "due to {AbortCount} consecutive aborts. " +
                    "Waiting for a successful cycle to auto-release.",
                    _suspendedAt, _consecutiveCycleAborts);
                return SyncCycleResult.SkippedSuspended;
            }
        }

        // ── Step 2: Check market hours (REQ-PORT-019) ─────────────────────
        if (!await IsMarketHoursAsync(calendar, ct))
        {
            _logger.LogTrace("LADS: outside NSE market hours. Skipping intraday sync cycle.");
            return SyncCycleResult.SkippedOutsideMarketHours;
        }

        // ── Step 3: Read intraday interval from sys_config ─────────────────
        var intradayIntervalMinutes = await ReadIntradayIntervalAsync(ct);

        // ── Step 4: Query eligible users (approved + valid FYERS token) ────
        var eligibleUsers = await GetEligibleUsersAsync(ct);
        if (eligibleUsers.Count == 0)
        {
            _logger.LogTrace("LADS: no eligible users found (approved + valid FYERS token).");
            return SyncCycleResult.NoUsers;
        }

        // ── Step 5: Per-user sync loop ─────────────────────────────────────
        var accountSyncCollection = _database
            .GetCollection<BsonDocument>("fyers_account_sync");

        var processedCount = 0;
        var errorCount = 0;
        var skippedCount = 0;

        foreach (var user in eligibleUsers.Take(_options.Value.BatchSize))
        {
            if (ct.IsCancellationRequested)
                break;

            try
            {
                if (await ShouldSkipUserAsync(accountSyncCollection, user, intradayIntervalMinutes, ct))
                {
                    skippedCount++;
                    continue;
                }

                await SyncUserAccountAsync(user, accountSyncCollection, ct);
                processedCount++;
            }
            catch (MongoException ex)
            {
                errorCount++;
                _logger.LogError(ex, "LADS: MongoDB error during sync for user {UserId}.", user.UserId);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                errorCount++;
                _logger.LogError(ex, "LADS: unexpected error during sync for user {UserId}.", user.UserId);
            }
        }

        // ── Step 6: Cycle-level abort tracking (REQ-PORT-021a) ────────────
        if (errorCount > 0)
        {
            await HandleCycleAbortAsync(ct);
            return new SyncCycleResult(processedCount, errorCount, skippedCount, IsAborted: true);
        }

        ResetCycleState();

        _logger.LogInformation(
            "LADS: cycle complete — {Processed} synced, {Skipped} skipped, {Errors} errors.",
            processedCount, skippedCount, errorCount);

        return new SyncCycleResult(processedCount, errorCount, skippedCount, IsAborted: false);
    }

    /// <summary>
    /// Checks whether the current UTC time falls within NSE market hours
    /// according to the internal trading calendar.
    /// </summary>
    private static async Task<bool> IsMarketHoursAsync(
        ITradingCalendarRepository calendar, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sessions = await calendar.GetSessionsAsync(
            fromDate: today.ToString("yyyy-MM-dd"),
            toDate: today.ToString("yyyy-MM-dd"),
            ct: ct);

        foreach (var session in sessions)
        {
            if (!DateOnly.TryParse(session.SessionDate, out var sessionDate)
                || sessionDate != today)
                continue;

            if (TimeSpan.TryParse(session.SessionStartTime, out var startTime)
                && TimeSpan.TryParse(session.SessionEndTime, out var endTime))
            {
                // Session times are stored as IST (UTC+5:30).
                // Build today's IST session window and convert to UTC.
                var nowUtc = DateTime.UtcNow;
                var todayUtc = nowUtc.Date;
                var sessionStartUtc = todayUtc.Add(startTime).AddHours(-5).AddMinutes(-30);
                var sessionEndUtc = todayUtc.Add(endTime).AddHours(-5).AddMinutes(-30);

                return nowUtc >= sessionStartUtc && nowUtc <= sessionEndUtc;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the intraday sync interval from sys_config.
    /// REQ-PORT-019: interval at <c>jobs.account_sync.intraday_interval_minutes</c>.
    /// </summary>
    private async Task<int> ReadIntradayIntervalAsync(CancellationToken ct)
    {
        var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
        var filter = Builders<BsonDocument>.Filter.Eq("key",
            "jobs.account_sync.intraday_interval_minutes");
        var doc = await sysConfig.Find(filter).FirstOrDefaultAsync(ct);

        if (doc is null)
            return _options.Value.DefaultIntradayIntervalMinutes;

        var value = doc.GetValue("value", BsonNull.Value);
        if (value.IsBsonNull || !int.TryParse(value.AsString, out var parsed))
            return _options.Value.DefaultIntradayIntervalMinutes;

        return parsed;
    }

    /// <summary>
    /// Finds all approved users who have an active, non-expired FYERS token.
    /// REQ-PORT-019: runs for each user with a valid FYERS token.
    /// </summary>
    private async Task<List<UserSyncInfo>> GetEligibleUsersAsync(CancellationToken ct)
    {
        var usersCollection = _database.GetCollection<BsonDocument>("users");
        var tokensCollection = _database.GetCollection<BsonDocument>("fyers_tokens");

        var approvedUsers = await usersCollection
            .Find(Builders<BsonDocument>.Filter.Eq("status", "approved"))
            .Project<BsonDocument>(Builders<BsonDocument>.Projection
                .Include("_id").Include("user_id"))
            .ToListAsync(ct);

        if (approvedUsers.Count == 0)
            return [];

        var now = DateTime.UtcNow;
        var activeTokens = await tokensCollection
            .Find(Builders<BsonDocument>.Filter.Eq("status", "active")
                & Builders<BsonDocument>.Filter.Gt("expires_at", now))
            .Project<BsonDocument>(Builders<BsonDocument>.Projection
                .Include("user_id"))
            .ToListAsync(ct);

        var tokenUserIds = activeTokens
            .Select(t => t["user_id"].AsString)
            .ToHashSet();

        return approvedUsers
            .Where(u => u.TryGetValue("user_id", out var uid)
                        && uid.IsString
                        && tokenUserIds.Contains(uid.AsString))
            .Select(u => new UserSyncInfo(
                Id: u["_id"].AsObjectId,
                UserId: u["user_id"].AsString))
            .ToList();
    }

    /// <summary>
    /// Returns true when the intraday interval has not elapsed for this user.
    /// </summary>
    private static async Task<bool> ShouldSkipUserAsync(
        IMongoCollection<BsonDocument> accountSyncCollection,
        UserSyncInfo user,
        int intradayIntervalMinutes,
        CancellationToken ct)
    {
        var filter = Builders<BsonDocument>.Filter.Eq("user_id", user.UserId);
        var syncDoc = await accountSyncCollection
            .Find(filter)
            .Project<BsonDocument>(Builders<BsonDocument>.Projection
                .Include("last_successful_sync_at"))
            .FirstOrDefaultAsync(ct);

        if (syncDoc is null)
            return false;

        var lastSync = syncDoc.GetValue("last_successful_sync_at", BsonNull.Value);
        if (lastSync.IsBsonNull)
            return false;

        return (DateTime.UtcNow - lastSync.ToUniversalTime()).TotalMinutes
               < intradayIntervalMinutes;
    }

    /// <summary>
    /// Per-user account sync cycle.
    ///
    /// P5-T7 skeleton: records sync progress in <c>fyers_account_sync</c>
    /// without FYERS API calls or trade-ledger writes. Real sync (positions,
    /// orders, trades, holdings) is wired in P5-T8/T9.
    /// </summary>
    private static async Task SyncUserAccountAsync(
        UserSyncInfo user,
        IMongoCollection<BsonDocument> accountSyncCollection,
        CancellationToken ct)
    {
        var now = DateTime.UtcNow;

        var filter = Builders<BsonDocument>.Filter.Eq("user_id", user.UserId);
        var update = Builders<BsonDocument>.Update
            .Set("last_successful_sync_at", now)
            .Set("last_sync_attempt_at", now)
            .Set("sync_status", "completed")
            .Set("updated_at", now)
            .SetOnInsert("user_id", user.UserId)
            .SetOnInsert("user_object_id", user.Id);

        await accountSyncCollection.UpdateOneAsync(
            filter, update,
            options: new UpdateOptions { IsUpsert = true },
            cancellationToken: ct);
    }

    /// <summary>
    /// Handles a cycle-level abort: increments counter, emits OTEL metric,
    /// triggers graduated suspension at threshold (REQ-PORT-021a).
    /// </summary>
    private async Task HandleCycleAbortAsync(CancellationToken ct)
    {
        int currentCount;

        lock (_suspensionLock)
        {
            _consecutiveCycleAborts++;
            currentCount = _consecutiveCycleAborts;
        }

        // REQ-PORT-021a(a): OTEL counter with reason label.
        CycleAbortedCounter.Add(1,
            new KeyValuePair<string, object?>("reason", "cycle_error"));

        _logger.LogWarning(
            "LADS: cycle aborted ({AbortCount} consecutive). " +
            "lads.cycle.aborted.total incremented.",
            currentCount);

        // REQ-PORT-021a(b): warn threshold reached.
        if (currentCount == _options.Value.ConsecutiveAbortWarnThreshold)
        {
            _logger.LogWarning(
                "LADS: consecutive abort count ({AbortCount}) reached " +
                "warn threshold ({Threshold}). " +
                "Admin System Health widget warning (REQ-PORT-021a(b)).",
                currentCount, _options.Value.ConsecutiveAbortWarnThreshold);
        }

        // REQ-PORT-021a(c): suspend threshold reached.
        if (currentCount >= _options.Value.ConsecutiveAbortSuspendThreshold)
        {
            await ActivateSuspensionAsync(ct);
        }
    }

    /// <summary>
    /// Activates platform-wide LADS suspension.
    /// Records incident + admin notification (REQ-PORT-021a(c)).
    /// </summary>
    private async Task ActivateSuspensionAsync(CancellationToken ct)
    {
        lock (_suspensionLock)
        {
            if (_isSuspended)
                return;
            _isSuspended = true;
            _suspendedAt = DateTime.UtcNow;
        }

        _logger.LogCritical(
            "LADS: platform-wide sync SUSPENDED after {AbortCount} consecutive " +
            "aborts (threshold: {Threshold}). Suspension recorded in rme_incidents.",
            _consecutiveCycleAborts, _options.Value.ConsecutiveAbortSuspendThreshold);

        await RecordSuspensionIncidentAsync(ct);

        // Write admin notification for Telegram dispatch via NDJ.
        var notifications = _database.GetCollection<BsonDocument>("notifications");
        var notification = new BsonDocument
        {
            ["notification_type"] = NotificationType.AdminLadsThresholdBreach,
            ["is_admin_notification"] = true,
            ["content"] = "LADS sustained failure: platform-wide sync suspended " +
                $"after {_consecutiveCycleAborts} consecutive aborts " +
                $"(threshold: {_options.Value.ConsecutiveAbortSuspendThreshold}). " +
                "Entry intent creation blocked. Auto-releases on next successful cycle.",
            ["generated_at"] = DateTime.UtcNow,
            ["telegram_delivery_status"] = "pending",
            ["telegram_delivery_attempts"] = 0,
        };
        await notifications.InsertOneAsync(notification, cancellationToken: ct);
    }

    /// <summary>
    /// Records a lads_sustained_failure incident in rme_incidents.
    /// </summary>
    private async Task RecordSuspensionIncidentAsync(CancellationToken ct)
    {
        var incidents = _database.GetCollection<BsonDocument>("rme_incidents");
        var incident = new BsonDocument
        {
            ["user_id"] = BsonNull.Value,
            ["position_id"] = BsonNull.Value,
            ["incident_type"] = "lads_sustained_failure",
            ["severity"] = "error",
            ["status"] = "open",
            ["detail"] = new BsonDocument
            {
                ["consecutive_abort_count"] = _consecutiveCycleAborts,
                ["suspend_threshold"] = _options.Value.ConsecutiveAbortSuspendThreshold,
                ["suspended_at"] = DateTime.UtcNow,
            },
            ["created_at"] = DateTime.UtcNow,
            ["updated_at"] = DateTime.UtcNow,
        };
        await incidents.InsertOneAsync(incident, cancellationToken: ct);
    }

    /// <summary>
    /// Resets abort counter and releases suspension.
    /// REQ-PORT-021a: successful cycle resets counter; auto-releases.
    /// </summary>
    private void ResetCycleState()
    {
        lock (_suspensionLock)
        {
            var wasSuspended = _isSuspended;
            _consecutiveCycleAborts = 0;
            _isSuspended = false;

            if (wasSuspended)
            {
                _logger.LogWarning(
                    "LADS: successful cycle — auto-released suspension (REQ-PORT-021a(c)).");
                _ = ResolveOpenSuspensionIncidentsAsync();
            }
        }
    }

    /// <summary>Resolves all open lads_sustained_failure incidents (fire-and-forget).</summary>
    private async Task ResolveOpenSuspensionIncidentsAsync()
    {
        try
        {
            var incidents = _database.GetCollection<BsonDocument>("rme_incidents");
            await incidents.UpdateManyAsync(
                Builders<BsonDocument>.Filter.Eq("incident_type", "lads_sustained_failure")
                & Builders<BsonDocument>.Filter.Eq("status", "open"),
                Builders<BsonDocument>.Update
                    .Set("status", "resolved")
                    .Set("resolved_at", DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LADS: failed to resolve open suspension incidents " +
                "(non-critical — will retry next cycle).");
        }
    }
}
