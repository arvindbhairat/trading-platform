using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Admin;
using SignalStack.Api.LedgerWriters;
using SignalStack.Api.Notifications;
using SignalStack.Configuration.Ledger;
using SignalStack.Worker.Observability;
using SignalStack.Worker.Rme;

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
    private readonly ILedgerWriteLock _ledgerLock;
    private readonly ILedgerSnapshotVersionHelper _snapshotVersionHelper;
    private readonly TradeIngestionService _ingestionService;
    private readonly IntentReconciliationService _reconciliationService;
    private readonly IPositionChannelRegistry _channelRegistry;
    private readonly IPositionRepository _positionRepository;

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
        ILogger<LiveAccountDataSyncService> logger,
        ILedgerWriteLock ledgerLock,
        ILedgerSnapshotVersionHelper snapshotVersionHelper,
        TradeIngestionService ingestionService,
        IntentReconciliationService reconciliationService,
        IPositionChannelRegistry channelRegistry,
        IPositionRepository positionRepository)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _ledgerLock = ledgerLock ?? throw new ArgumentNullException(nameof(ledgerLock));
        _snapshotVersionHelper = snapshotVersionHelper ?? throw new ArgumentNullException(nameof(snapshotVersionHelper));
        _ingestionService = ingestionService ?? throw new ArgumentNullException(nameof(ingestionService));
        _reconciliationService = reconciliationService ?? throw new ArgumentNullException(nameof(reconciliationService));
        _channelRegistry = channelRegistry ?? throw new ArgumentNullException(nameof(channelRegistry));
        _positionRepository = positionRepository ?? throw new ArgumentNullException(nameof(positionRepository));
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

                // P5-T9: trade data from FYERS intraday API to be passed here.
                // For now, empty list — ingestion is a no-op when no trades.
                await SyncUserAccountAsync(user, accountSyncCollection, [], ct);
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

        // ── Step 6: Platform-wide callback-failure ratio check (P7-T10) ──
        // Evaluates the LADS-only match ratio and fires an admin alert when
        // the ratio exceeds orders.callback_failure_alert_ratio.
        // Runs after all users are reconciled, regardless of errors, because
        // partial reconciliation data still carries signal.
        // REQ-ORDER-015d.
        try
        {
            await _reconciliationService.EvaluateCallbackRatioAsync(ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LADS: callback ratio evaluation failed. " +
                "This does not affect the sync cycle outcome.");
        }

        // ── Step 7: Cycle-level abort tracking (REQ-PORT-021a) ────────────
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
    /// Reads the intent timeout from sys_config (REQ-ORDER-015c).
    /// Default: <c>orders.intent_timeout_minutes</c> or 10 if not configured.
    /// </summary>
    private async Task<int> ReadIntentTimeoutMinutesAsync(CancellationToken ct)
    {
        var sysConfig = _database.GetCollection<BsonDocument>("sys_config");
        var filter = Builders<BsonDocument>.Filter.Eq("key", "orders.intent_timeout_minutes");
        var doc = await sysConfig.Find(filter).FirstOrDefaultAsync(ct);

        if (doc is null)
            return 10;

        var value = doc.GetValue("value", BsonNull.Value);
        if (value.IsBsonNull || !int.TryParse(value.AsString, out var parsed))
            return 10;

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
    /// Per-user account sync cycle with trade-ledger write lock acquisition
    /// and trade ingestion (REQ-PORT-031/031a/031b, REQ-PORT-005a, REQ-PORT-023).
    ///
    /// Acquires the per-user lock in TryNonBlocking mode. If busy, the user
    /// is skipped (background sync behaviour). On successful lock acquisition,
    /// ingests the provided trades, records sync progress, and increments
    /// <c>ledger_snapshot_version</c> after the durable write. The fencing
    /// token guards against stale writes; if a fencing abort is detected
    /// the version is NOT incremented (REQ-PORT-031b invariant A-11).
    /// </summary>
    private async Task SyncUserAccountAsync(
        UserSyncInfo user,
        IMongoCollection<BsonDocument> accountSyncCollection,
        IReadOnlyList<RawTrade> rawTrades,
        CancellationToken ct)
    {
        // ── Step 1: Acquire the per-user trade-ledger write lock ────────────
        var acquireResult = await _ledgerLock.AcquireAsync(
            user.UserId, LedgerAcquireMode.TryNonBlocking, ct);

        if (acquireResult.Outcome != LedgerLockAcquireOutcome.Acquired)
        {
            _logger.LogTrace(
                "LADS: lock not acquired for user {UserId} ({Outcome}). Skipping this cycle.",
                user.UserId, acquireResult.Outcome);
            return;
        }

        await using var handle = acquireResult.Handle!;

        try
        {
            // ── Step 2: Ingest trades (REQ-PORT-005a/023) ──────────────────
            if (rawTrades.Count > 0)
            {
                _logger.LogInformation(
                    "LADS: ingesting {Count} trades for user {UserId} (fence {FenceToken}).",
                    rawTrades.Count, user.UserId, handle.FencingToken);

                var ingestionResult = await _ingestionService.IngestAsync(
                    user.UserId, rawTrades, handle.FencingToken, SyncSource.Intraday,
                    DateTime.UtcNow, ct: ct);

                _logger.LogInformation(
                    "LADS: ingestion result for user {UserId} — " +
                    "{Inserted} inserted, {Duplicates} duplicates, {Excluded} excluded.",
                    user.UserId,
                    ingestionResult.Inserted,
                    ingestionResult.SkippedDuplicates,
                    ingestionResult.SkippedNonNifty500);
            }
            else
            {
                _logger.LogTrace(
                    "LADS: no trades to ingest for user {UserId} (fence {FenceToken}).",
                    user.UserId, handle.FencingToken);
            }

            // ── Step 2a: Emit FillConfirmedEvent for matched PendingEntry positions ─
            // REQ-RME-CONC-005: wire LADS as event producer through the per-position
            // channel registry.
            var fillDetails = new List<FillNotificationDetail>();
            if (rawTrades.Count > 0)
            {
                fillDetails = await EmitFillConfirmedEventsAsync(user.UserId, rawTrades, ct);

                // ── Step 2a(i): Write fill notifications (REQ-ORDER-015f) ─────
                // Fill notifications (Telegram + portal feed) fire on LADS-confirmed
                // fill evidence, not on the FYERS finished callback. Partial-fill
                // vs full-fill distinction is determined here.
                if (fillDetails.Count > 0)
                {
                    await WriteFillNotificationsAsync(user.UserId, user.Id, fillDetails, ct);
                }
            }

            // ── Step 2b: LADS intent-reconciliation safety net (REQ-ORDER-015c) ──
            // Runs after trade ingestion to reconcile observed FYERS trades against
            // intent_ledger records. Handles callback-matched stamping, callback-missed
            // promotion, unresolved timeout, and orphan detection.
            // Non-critical: failures are logged but do not block the sync progress write.
            try
            {
                var intentTimeoutMinutes = await ReadIntentTimeoutMinutesAsync(ct);
                await _reconciliationService.ReconcileAsync(
                    user.UserId, user.Id, rawTrades, intentTimeoutMinutes, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "LADS: intent reconciliation failed for user {UserId}. " +
                    "Trade ingestion completed; reconciliation will retry next cycle.",
                    user.UserId);
            }

            // ── Step 3: Record sync progress (P5-T7 guard write) ───────────
            var now = DateTime.UtcNow;

            var filter = Builders<BsonDocument>.Filter.Eq("user_id", user.UserId);
            var update = Builders<BsonDocument>.Update
                .Set("last_successful_sync_at", now)
                .Set("last_sync_attempt_at", now)
                .Set("sync_status", "completed")
                .Set("updated_at", now)
                .SetOnInsert("user_id", user.UserId)
                .SetOnInsert("user_object_id", user.Id);

            // REQ-PORT-031b(b): include fencing-token filter for stale-write detection
            filter = Builders<BsonDocument>.Filter.Eq("user_id", user.UserId)
                     & Builders<BsonDocument>.Filter.Lte("ledger_fence_token", handle.FencingToken);

            var result = await accountSyncCollection.UpdateOneAsync(
                filter, update,
                options: new UpdateOptions { IsUpsert = true },
                cancellationToken: ct);

            // ── Step 4: Fencing-token guard (REQ-PORT-031b invariant A-11) ──
            // If the update matched 0 documents, another process has advanced
            // the fence token past ours — do NOT increment the snapshot version.
            if (result.MatchedCount == 0)
            {
                _logger.LogWarning(
                    "LADS: fencing-token abort for user {UserId} " +
                    "(fence {FenceToken}). ledger_snapshot_version NOT incremented.",
                    user.UserId, handle.FencingToken);
                return;
            }

            // ── Step 5: Increment ledger_snapshot_version after durable write ─
            // REQ-PORT-031a: only after all trade-ledger writes durably committed.
            await _snapshotVersionHelper.IncrementAsync(user.UserId, ct);
        }
        catch
        {
            // Ensure the lock is released on any failure.
            // The handle's DisposeAsync will call ReleaseAsync.
            throw;
        }
    }

    /// <summary>
    /// Details about a detected fill used for notification dispatch (REQ-ORDER-015f).
    /// </summary>
    private sealed record FillNotificationDetail(
        string Symbol,
        string PositionId,
        int RequestedQuantity,
        int FilledQuantity,
        decimal AvgFillPrice,
        bool IsPartialFill);

    /// <summary>
    /// Queries PendingEntry positions for the user and emits FillConfirmedEvent
    /// through the channel registry for each position whose symbol matches an
    /// ingested buy-side trade.
    /// REQ-RME-CONC-005: LADS fill-detection → per-position channel dispatch.
    /// Returns fill details for notification dispatch (REQ-ORDER-015f).
    /// </summary>
    private async Task<List<FillNotificationDetail>> EmitFillConfirmedEventsAsync(
        string userId, IReadOnlyList<RawTrade> rawTrades, CancellationToken ct)
    {
        var fillDetails = new List<FillNotificationDetail>();

        try
        {
            // Find buy-side trades that indicate a fill
            var buyTrades = rawTrades
                .Where(t => string.Equals(t.Side, "buy", StringComparison.OrdinalIgnoreCase)
                            || string.Equals(t.Side, "b", StringComparison.OrdinalIgnoreCase))
                .GroupBy(t => t.Symbol)
                .ToDictionary(g => g.Key, g => g.ToList());

            if (buyTrades.Count == 0)
                return fillDetails;

            // Query PendingEntry positions for this user
            var pendingPositions = await _positionRepository.GetByUserIdAndStateAsync(
                userId, PositionState.PendingEntry, ct);

            if (pendingPositions.Count == 0)
                return fillDetails;

            foreach (var position in pendingPositions)
            {
                if (!buyTrades.TryGetValue(position.Symbol, out var trades))
                    continue;

                // Aggregate fill quantity and average price
                var totalQty = trades.Sum(t => t.Quantity);
                var avgPrice = trades.Count > 0
                    ? trades.Average(t => t.Price)
                    : 0m;

                var fillEvent = new FillConfirmedEvent
                {
                    PositionId = position.PositionId,
                    OccurredAt = DateTimeOffset.UtcNow,
                    Source = "LADS",
                    FilledQty = totalQty,
                    AvgFillPrice = avgPrice,
                };

                await _channelRegistry.EnqueueAsync(fillEvent, ct);

                _logger.LogInformation(
                    "LADS: emitted FillConfirmedEvent for position {PositionId} " +
                    "(symbol: {Symbol}, qty: {Qty}, avgPrice: {Price}).",
                    position.PositionId, position.Symbol, totalQty, avgPrice);

                // Collect fill details for notification dispatch (REQ-ORDER-015f)
                var requestedQty = position.EntryQuantity ?? totalQty;
                fillDetails.Add(new FillNotificationDetail(
                    Symbol: position.Symbol,
                    PositionId: position.PositionId.ToString(),
                    RequestedQuantity: requestedQty,
                    FilledQuantity: totalQty,
                    AvgFillPrice: avgPrice,
                    IsPartialFill: totalQty < requestedQty));
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LADS: failed to emit FillConfirmedEvent for user {UserId}. " +
                "Trade ingestion completed; fill event emission is non-critical.",
                userId);
        }

        return fillDetails;
    }

    /// <summary>
    /// Writes fill notifications (Telegram + portal feed) for LADS-confirmed fills.
    /// REQ-ORDER-015f: fill notifications fire on LADS-confirmed fill evidence,
    /// not on the FYERS finished callback. Detects partial vs full fills.
    /// </summary>
    private async Task WriteFillNotificationsAsync(
        string userId,
        ObjectId userObjectId,
        IReadOnlyList<FillNotificationDetail> fillDetails,
        CancellationToken ct)
    {
        try
        {
            var notifications = _database.GetCollection<BsonDocument>("notifications");

            foreach (var detail in fillDetails)
            {
                var notificationType = detail.IsPartialFill
                    ? NotificationType.EntryPartiallyFilled
                    : NotificationType.EntryFilled;

                var fillSummary = detail.IsPartialFill
                    ? $"filled {detail.FilledQuantity} of {detail.RequestedQuantity} shares at ₹{detail.AvgFillPrice:F2}"
                    : $"filled {detail.FilledQuantity} shares at ₹{detail.AvgFillPrice:F2}";

                var content = detail.IsPartialFill
                    ? $"Entry partially filled for {detail.Symbol} — {fillSummary}. Review your positions."
                    : $"Entry filled for {detail.Symbol} — {fillSummary}.";

                var notification = new BsonDocument
                {
                    ["notification_type"] = notificationType,
                    ["is_admin_notification"] = false,
                    ["user_id"] = userObjectId,
                    ["symbol"] = detail.Symbol,
                    ["content"] = content,
                    ["generated_at"] = DateTime.UtcNow,
                    ["is_read"] = false,
                    ["telegram_delivery_status"] = "pending",
                    ["telegram_delivery_attempts"] = 0,
                };

                await notifications.InsertOneAsync(notification, cancellationToken: ct);

                _logger.LogInformation(
                    "LADS: wrote {NotificationType} notification for user {UserId} " +
                    "(symbol: {Symbol}, qty: {FilledQty}/{RequestedQty}).",
                    notificationType, userId, detail.Symbol,
                    detail.FilledQuantity, detail.RequestedQuantity);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex,
                "LADS: failed to write fill notifications for user {UserId}. " +
                "Fill events were emitted; notification dispatch is non-critical.",
                userId);
        }
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
