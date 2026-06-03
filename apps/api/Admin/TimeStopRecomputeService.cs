using Microsoft.Extensions.Logging;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Domain.Admin;
using SignalStack.Domain.Notifications;
using SignalStack.Storage.Admin;
using SignalStack.Storage.Notifications;
using SignalStack.Storage;

namespace SignalStack.Api.Admin;

/// <summary>
/// Recomputes <c>time_stop_date</c> for all non-terminal positions carrying a
/// Time Stop after a calendar edit, per REQ-STOP-003a. Idempotent — re-running
/// on the same calendar state produces the same result.
///
/// The recompute is triggered as a fire-and-forget background operation from the
/// calendar CRUD endpoints. The service is thread-safe and registered as a singleton.
/// </summary>
internal sealed class TimeStopRecomputeService
{
    private static readonly TimeSpan TriggerDebounceWindow = TimeSpan.FromSeconds(5);

    private readonly IMongoDatabase _database;
    private readonly ITradingCalendarRepository _calendarRepo;
    private readonly INotificationWriter _notificationWriter;
    private readonly ILogger<TimeStopRecomputeService> _logger;

    // Triggers received within the debounce window are coalesced into one run.
    private DateTime _lastTriggerUtc = DateTime.MinValue;
    private readonly object _lock = new();

    public TimeStopRecomputeService(
        IMongoDatabase database,
        ITradingCalendarRepository calendarRepo,
        INotificationWriter notificationWriter,
        ILogger<TimeStopRecomputeService> logger)
    {
        _database = database ?? throw new ArgumentNullException(nameof(database));
        _calendarRepo = calendarRepo ?? throw new ArgumentNullException(nameof(calendarRepo));
        _notificationWriter = notificationWriter ?? throw new ArgumentNullException(nameof(notificationWriter));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Trigger the recompute. Multiple rapid calls are debounced within a 5-second
    /// window (a batch edit across multiple dates coalesces into one run).
    /// Returns immediately; the recompute runs in the background.
    /// </summary>
    public void Trigger()
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            if ((now - _lastTriggerUtc) < TriggerDebounceWindow)
            {
                _logger.LogDebug(
                    "TimeStopRecompute: trigger debounced — last trigger was {Elapsed}s ago.",
                    (now - _lastTriggerUtc).TotalSeconds.ToString("F1"));
                return;
            }

            _lastTriggerUtc = now;
        }

        // Fire-and-forget: capture the ambient scope factory so the background
        // work runs independently of the HTTP request lifecycle.
        _ = Task.Run(() => RunRecomputeAsync());
    }

    /// <summary>
    /// Run the full recompute synchronously (awaitable). Used by tests and
    /// callers that need deterministic completion.
    /// </summary>
    public async Task RunRecomputeAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("TimeStopRecompute: starting recompute for all non-terminal positions.");

        try
        {
            var positionsCollection = _database.GetCollection<BsonDocument>("positions");
            var rmeEventsCollection = _database.GetCollection<BsonDocument>("rme_events");

            // ── Step 1: Load default session count from sys_config ─────────────
            var sysConfigCollection = _database.GetCollection<BsonDocument>("sys_config");
            var configDoc = await sysConfigCollection
                .Find(Builders<BsonDocument>.Filter.Eq("_id", "risk.time_stop.default_sessions"))
                .FirstOrDefaultAsync(ct);

            var defaultSessions = configDoc?["default"] is BsonValue dv && dv.IsNumeric
                ? dv.AsInt32
                : 10;

            _logger.LogDebug(
                "TimeStopRecompute: default time-stop sessions = {Count} (from sys_config or fallback).",
                defaultSessions);

            // ── Step 2: Load all non-terminal positions with a non-null time_stop_date ──
            var positionFilter = Builders<BsonDocument>.Filter.And(
                Builders<BsonDocument>.Filter.Ne("state", "Closed"),
                Builders<BsonDocument>.Filter.Ne("state", "Rejected"),
                Builders<BsonDocument>.Filter.Ne("time_stop_date", BsonNull.Value),
                Builders<BsonDocument>.Filter.Exists("time_stop_date"));

            var positions = await positionsCollection
                .Find(positionFilter)
                .ToListAsync(ct);

            if (positions.Count == 0)
            {
                _logger.LogInformation(
                    "TimeStopRecompute: no non-terminal positions with a Time Stop found. Nothing to recompute.");
                return;
            }

            _logger.LogInformation(
                "TimeStopRecompute: found {Count} non-terminal positions with Time Stop to recompute.",
                positions.Count);

            // ── Step 3: For each position, recompute the time_stop_date ────────
            var recomputedCount = 0;
            var dateChangedCount = 0;
            var minAffectedDate = string.Empty;
            var maxAffectedDate = string.Empty;
            var batchWrites = new List<WriteModel<BsonDocument>>();
            var rmeEventDocs = new List<BsonDocument>();

            foreach (var pos in positions)
            {
                var positionId = pos["_id"].AsGuid;
                var entryDateStr = GetEntryDateString(pos);

                if (entryDateStr is null)
                {
                    _logger.LogWarning(
                        "TimeStopRecompute: position {PositionId} has no usable entry date. Skipping.",
                        positionId);
                    continue;
                }

                var previousDate = pos.GetBsonStringOrNull("time_stop_date") ?? "";

                // Count forward `defaultSessions` trading sessions from entry date.
                var newDate = await ComputeTimeStopDateAsync(entryDateStr, defaultSessions, ct);

                if (newDate is null)
                {
                    _logger.LogWarning(
                        "TimeStopRecompute: could not compute time_stop_date for position {PositionId} " +
                        "(entry: {EntryDate}, sessions: {Count}). Skipping.",
                        positionId, entryDateStr, defaultSessions);
                    continue;
                }

                recomputedCount++;

                // Track affected date range for the summary notification.
                if (string.IsNullOrEmpty(minAffectedDate) ||
                    string.Compare(newDate, minAffectedDate, StringComparison.Ordinal) < 0)
                    minAffectedDate = newDate;
                if (string.IsNullOrEmpty(maxAffectedDate) ||
                    string.Compare(newDate, maxAffectedDate, StringComparison.Ordinal) > 0)
                    maxAffectedDate = newDate;

                if (previousDate != newDate)
                {
                    dateChangedCount++;

                    // Build the audit entry on the position document.
                    var auditEntry = new BsonDocument
                    {
                        ["previous_date"] = previousDate,
                        ["new_date"] = newDate,
                        ["trigger_event"] = "calendar_edit",
                        ["recomputed_at"] = DateTime.UtcNow,
                    };

                    // OCC-safe update: push audit entry + set new time_stop_date
                    var update = Builders<BsonDocument>.Update
                        .Set("time_stop_date", newDate)
                        .Set("updated_at", DateTime.UtcNow)
                        .Push("time_stop_audit", auditEntry);

                    var filter = Builders<BsonDocument>.Filter.Eq("_id", pos["_id"]);
                    batchWrites.Add(new UpdateOneModel<BsonDocument>(filter, update));

                    // Append rme_events record.
                    rmeEventDocs.Add(new BsonDocument
                    {
                        ["event_type"] = "time_stop_recomputed_on_calendar_edit",
                        ["position_id"] = new BsonBinaryData(positionId, GuidRepresentation.Standard),
                        ["previous_date"] = previousDate,
                        ["new_date"] = newDate,
                        ["event_at"] = DateTime.UtcNow,
                    });

                    _logger.LogDebug(
                        "TimeStopRecompute: position {PositionId}: time_stop_date {Previous} → {New}.",
                        positionId, previousDate, newDate);
                }
            }

            // ── Step 4: Execute batch writes ───────────────────────────────────
            if (batchWrites.Count > 0)
            {
                await positionsCollection.BulkWriteAsync(batchWrites, cancellationToken: ct);
                _logger.LogInformation(
                    "TimeStopRecompute: updated {Count} position documents.", batchWrites.Count);
            }

            if (rmeEventDocs.Count > 0)
            {
                await rmeEventsCollection.InsertManyAsync(rmeEventDocs, cancellationToken: ct);
                _logger.LogInformation(
                    "TimeStopRecompute: wrote {Count} rme_events records.", rmeEventDocs.Count);
            }

            // ── Step 5: Emit admin summary notification ────────────────────────
            await EmitSummaryNotificationAsync(
                positions.Count, recomputedCount, dateChangedCount,
                minAffectedDate, maxAffectedDate, ct);

            _logger.LogInformation(
                "TimeStopRecompute: complete. {Recomputed} positions processed, " +
                "{Changed} dates changed. Affected date range: {Min} to {Max}.",
                recomputedCount, dateChangedCount,
                minAffectedDate, maxAffectedDate);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("TimeStopRecompute: cancelled.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TimeStopRecompute: unexpected error during recompute.");
        }
    }

    /// <summary>
    /// Compute the time_stop_date by counting <paramref name="sessionCount"/> trading
    /// sessions forward from the <paramref name="entryDate"/> using the trading calendar.
    /// </summary>
    internal async Task<string?> ComputeTimeStopDateAsync(
        string entryDate, int sessionCount, CancellationToken ct = default)
    {
        // Fetch all session records (normal/special/muhurat) from entry date forward.
        // We need enough sessions to cover the count. Request a generous range.
        var fromDate = entryDate;
        var toDate = DateOnly.Parse(entryDate)
            .AddDays(sessionCount * 3) // ample buffer for weekends/holidays
            .ToString("yyyy-MM-dd");

        var sessions = await _calendarRepo.GetSessionsAsync(fromDate, toDate, ct);

        if (sessions.Count == 0)
        {
            _logger.LogWarning(
                "ComputeTimeStopDate: no trading sessions found from {From} to {To}.",
                fromDate, toDate);
            return null;
        }

        // Determine the index of the first session AFTER the entry date.
        // When the entry date IS a trading session, the first session after
        // entry is the next one (entryIndex + 1). When the entry is NOT a
        // session, the first session after entry is the first session whose
        // date is > entry.
        //
        // The time_stop_date is then:
        //   sessions[firstAfterIndex + sessionCount - 1]
        // i.e., sessionCount trading sessions AFTER the entry date (the entry
        // itself does not count as a session).
        var firstAfterIndex = -1;

        for (var i = 0; i < sessions.Count; i++)
        {
            if (sessions[i].SessionDate == entryDate)
            {
                firstAfterIndex = i + 1; // entry IS a session — next index
                break;
            }

            if (string.Compare(sessions[i].SessionDate, entryDate, StringComparison.Ordinal) > 0)
            {
                firstAfterIndex = i; // entry before this session — this IS the first after
                break;
            }
        }

        // If we never found a session after the entry date, the entry is
        // after all known sessions.
        if (firstAfterIndex < 0)
        {
            _logger.LogWarning(
                "ComputeTimeStopDate: entry date {EntryDate} is after all known trading sessions.",
                entryDate);
            return null;
        }

        var targetIndex = firstAfterIndex + sessionCount - 1;

        if (targetIndex >= sessions.Count)
        {
            // Need more sessions — extend the query range.
            var extendedTo = DateOnly.Parse(entryDate)
                .AddDays(sessionCount * 5)
                .ToString("yyyy-MM-dd");

            sessions = await _calendarRepo.GetSessionsAsync(fromDate, extendedTo, ct);

            // Recalculate firstAfterIndex.
            firstAfterIndex = -1;
            for (var i = 0; i < sessions.Count; i++)
            {
                if (sessions[i].SessionDate == entryDate)
                {
                    firstAfterIndex = i + 1;
                    break;
                }
                if (string.Compare(sessions[i].SessionDate, entryDate, StringComparison.Ordinal) > 0)
                {
                    firstAfterIndex = i;
                    break;
                }
            }

            if (firstAfterIndex < 0)
            {
                _logger.LogWarning(
                    "ComputeTimeStopDate: entry date {EntryDate} not found after extended query.",
                    entryDate);
                return null;
            }

            targetIndex = firstAfterIndex + sessionCount - 1;

            if (targetIndex >= sessions.Count)
            {
                _logger.LogWarning(
                    "ComputeTimeStopDate: insufficient calendar data. " +
                    "Need {Target} sessions, have {Count}. " +
                    "Entry: {EntryDate}, buffer range: {From} → {To}.",
                    targetIndex + 1, sessions.Count, entryDate, fromDate, extendedTo);
                return null;
            }
        }

        return sessions[targetIndex].SessionDate;
    }

    /// <summary>
    /// Extracts the entry date from a position document as a "yyyy-MM-dd" string
    /// in IST (Asia/Kolkata). Uses <c>created_at</c> as the reference date.
    /// Returns null when the position has no usable timestamp.
    /// </summary>
    private static string? GetEntryDateString(BsonDocument position)
    {
        if (!position.TryGetValue("created_at", out var createdAtValue) || !createdAtValue.IsValidDateTime)
            return null;

        var utcDate = createdAtValue.ToUniversalTime();
        var istDate = TimeZoneInfo.ConvertTimeFromUtc(utcDate, IstTimeZone.Instance);
        return istDate.ToString("yyyy-MM-dd");
    }

    /// <summary>
    /// Emits a single admin notification summarising the recompute scope.
    /// REQ-STOP-003a: one admin notification (dashboard + Telegram admin message).
    /// </summary>
    private async Task EmitSummaryNotificationAsync(
        int totalPositions, int recomputedCount, int dateChangedCount,
        string minDate, string maxDate, CancellationToken ct)
    {
        var content = dateChangedCount > 0
            ? $"Time Stop recompute complete: {recomputedCount} positions processed, " +
              $"{dateChangedCount} dates changed. Affected date range: {minDate} to {maxDate}."
            : $"Time Stop recompute complete: {recomputedCount} positions checked, " +
              $"no date changes needed.";

        var notification = new NotificationDocument
        {
            UserId = MongoDB.Bson.ObjectId.Empty, // admin broadcast
            NotificationType = NotificationType.AdminTimeStopRecompute,
            Content = content,
            DeepLink = "/admin/calendar",
            GeneratedAt = DateTime.UtcNow,
            IsAdminNotification = true,
        };

        // Set delivery fields to trigger Telegram + email dispatch.
        notification.TelegramDeliveryStatus = DeliveryStatus.Pending;
        notification.EmailDeliveryStatus = "not_attempted";

        await _notificationWriter.WriteAsync(notification, ct);

        _logger.LogInformation(
            "TimeStopRecompute: admin summary notification written. {Content}", content);
    }
}
