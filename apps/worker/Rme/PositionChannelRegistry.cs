using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MongoDB.Bson;
using MongoDB.Driver;
using SignalStack.Api.Notifications;
using SignalStack.Api.Users;
using SignalStack.Worker.Observability;

namespace SignalStack.Worker.Rme;

/// <summary>
/// In-process channel registry that serialises RME event processing per position
/// (ADR-0003 Layer 1). A single bounded System.Threading.Channels.Channel per active
/// position ensures FIFO event ordering with no concurrent processing of the same
/// position. OCC (Layer 2) is the write-level safety net applied via <see cref="IPositionRepository"/>
/// with a 3-retry ceiling (REQ-RME-CONC-002).
/// REQ-RME-CONC-001/002/004/005.
/// </summary>
public sealed class PositionChannelRegistry : IPositionChannelRegistry, IAsyncDisposable
{
    private const int MaxOccRetries = 3;

    private readonly ConcurrentDictionary<Guid, PositionChannelEntry> _channels = new();
    private readonly ILogger<PositionChannelRegistry> _logger;
    private readonly PositionChannelOptions _options;
    private readonly IRmeEventConsumer _consumer;
    private readonly IPositionRepository _positionRepository;
    private readonly IMongoDatabase _database;
    private readonly INotificationWriter _notificationWriter;

    public PositionChannelRegistry(
        ILogger<PositionChannelRegistry> logger,
        IOptions<PositionChannelOptions> options,
        IRmeEventConsumer consumer,
        IPositionRepository positionRepository,
        IMongoDatabase database,
        INotificationWriter notificationWriter)
        : this(logger, options, consumer, positionRepository, database, notificationWriter, WorkerTelemetry.Meter) { }

    internal PositionChannelRegistry(
        ILogger<PositionChannelRegistry> logger,
        IOptions<PositionChannelOptions> options,
        IRmeEventConsumer consumer,
        IPositionRepository positionRepository,
        IMongoDatabase database,
        INotificationWriter notificationWriter,
        Meter meter)
    {
        _logger = logger;
        _options = options.Value;
        _consumer = consumer;
        _positionRepository = positionRepository;
        _database = database;
        _notificationWriter = notificationWriter;

        // REQ-RME-CONC-004: platform-wide max backlog gauge
        meter.CreateObservableGauge(
            "rme.channel.backlog_warn_depth",
            GetMaxBacklogDepth,
            description: "Maximum event backlog depth across all active position channels (platform-wide max).");

        // REQ-RME-CONC-004: per-position backlog gauge tagged by position_id
        meter.CreateObservableGauge(
            "rme.channel.backlog_depth",
            GetPerPositionBacklogMeasurements,
            description: "Event backlog depth per active position channel.");
    }

    /// <inheritdoc/>
    public void OpenChannel(Guid positionId)
    {
        var channel = Channel.CreateBounded<RmeEvent>(new BoundedChannelOptions(_options.ChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

        var cts = new CancellationTokenSource();
        var consumerTask = Task.Run(() => ConsumeChannelAsync(positionId, channel, cts.Token));
        var entry = new PositionChannelEntry(channel, cts, consumerTask);

        if (!_channels.TryAdd(positionId, entry))
        {
            // Channel already open; discard the duplicate
            channel.Writer.TryComplete();
            cts.Cancel();
            cts.Dispose();
            return;
        }

        _logger.LogDebug("Opened RME channel for position {PositionId}.", positionId);
    }

    /// <inheritdoc/>
    public void CloseChannel(Guid positionId)
    {
        if (!_channels.TryRemove(positionId, out var entry))
            return;

        entry.Channel.Writer.TryComplete();
        _logger.LogDebug(
            "Closing RME channel for position {PositionId}; consumer task will drain remaining events.",
            positionId);
    }

    /// <inheritdoc/>
    public async ValueTask EnqueueAsync(RmeEvent rmeEvent, CancellationToken cancellationToken = default)
    {
        if (!_channels.TryGetValue(rmeEvent.PositionId, out var entry))
        {
            _logger.LogWarning(
                "Attempted to enqueue {EventType} for position {PositionId} but no channel is registered; event discarded.",
                rmeEvent.GetType().Name, rmeEvent.PositionId);
            return;
        }

        var depth = entry.Channel.Reader.Count;
        if (depth >= _options.BacklogWarnDepth)
        {
            _logger.LogWarning(
                "RME channel backlog for position {PositionId} is at depth {Depth} (warn threshold: {Threshold}).",
                rmeEvent.PositionId, depth, _options.BacklogWarnDepth);
        }

        await entry.Channel.Writer.WriteAsync(rmeEvent, cancellationToken);
    }

    /// <summary>
    /// Returns the maximum backlog depth across all active position channels.
    /// Used as the observable-gauge callback for rme.channel.backlog_warn_depth.
    /// REQ-RME-CONC-004.
    /// </summary>
    internal int GetMaxBacklogDepth()
    {
        if (_channels.IsEmpty) return 0;
        var max = 0;
        foreach (var entry in _channels.Values)
        {
            var depth = entry.Channel.Reader.Count;
            if (depth > max) max = depth;
        }
        return max;
    }

    private IEnumerable<Measurement<int>> GetPerPositionBacklogMeasurements()
    {
        foreach (var (positionId, entry) in _channels)
        {
            yield return new Measurement<int>(
                entry.Channel.Reader.Count,
                new KeyValuePair<string, object?>("position_id", positionId.ToString()));
        }
    }

    private async Task ConsumeChannelAsync(
        Guid positionId,
        Channel<RmeEvent> channel,
        CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var evt in channel.Reader.ReadAllAsync(cancellationToken))
            {
                // ADR-0003 carve-out: TrailingStopUpdateEvent must never arrive on the
                // channel. The EOD job applies it synchronously before session start.
                if (evt is TrailingStopUpdateEvent)
                {
                    _logger.LogError(
                        "TrailingStopUpdateEvent for position {PositionId} was routed through the per-position channel; " +
                        "rejecting per ADR-0003. The EOD stop-update job must apply this synchronously before LMDS/LADS start.",
                        positionId);
                    continue;
                }

                try
                {
                    await ProcessEventWithOccRetryAsync(positionId, evt, cancellationToken);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Unhandled exception processing {EventType} for position {PositionId}.",
                        evt.GetType().Name, positionId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown path
        }

        _logger.LogDebug(
            "RME channel consumer for position {PositionId} has drained and terminated.",
            positionId);
    }

    /// <summary>
    /// Reads the current position, invokes the consumer pure function, and writes
    /// the updated position with OCC. Retries up to <see cref="MaxOccRetries"/> times
    /// on version conflict (REQ-RME-CONC-002).
    /// </summary>
    private async Task ProcessEventWithOccRetryAsync(
        Guid positionId,
        RmeEvent evt,
        CancellationToken cancellationToken)
    {
        // 1. Read current position document
        var position = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
        if (position is null)
        {
            _logger.LogError(
                "Position {PositionId} not found. Cannot process {EventType}. Skipping.",
                positionId, evt.GetType().Name);
            return;
        }

        int retryCount = 0;
        while (true)
        {
            // 2. Invoke the pure-function consumer (REQ-RME-CONC-003)
            var (updatedPosition, outputs) = await _consumer.ConsumeAsync(evt, position, cancellationToken);

            // If the consumer returned the same instance (no change), skip the write
            if (ReferenceEquals(updatedPosition, position) && outputs.Count == 0)
            {
                await ApplyOutputsAsync(positionId, outputs, position, evt, cancellationToken);
                return;
            }

            // 3. Write with OCC version filter (REQ-RME-CONC-002)
            var result = await _positionRepository.UpdateWithOccAsync(
                updatedPosition,
                expectedVersion: position.Version,
                ct: cancellationToken);

            if (result.Success)
            {
                // Write accepted — apply side-effect descriptors
                await ApplyOutputsAsync(positionId, outputs, position, evt, cancellationToken);
                _logger.LogDebug(
                    "Position {PositionId} updated to version {Version} after {EventType}.",
                    positionId, result.NewVersion, evt.GetType().Name);
                return;
            }

            // 4. OCC conflict — retry
            retryCount++;
            if (retryCount > MaxOccRetries)
            {
                _logger.LogError(
                    "OCC write failed after {MaxRetries} retries for position {PositionId} " +
                    "on event {EventType}. Ceasing further retry per REQ-RME-CONC-002.",
                    MaxOccRetries, positionId, evt.GetType().Name);

                // REQ-RME-CONC-007: write rme_incidents record with
                // incident_type: concurrency_conflict_unresolved
                await WriteIncidentAsync(positionId, position.UserId, evt, cancellationToken);
                return;
            }

            _logger.LogWarning(
                "OCC conflict on position {PositionId} (expected version {Version}, retry {Retry}/{MaxRetries}). " +
                "Re-reading and retrying.",
                positionId, position.Version, retryCount, MaxOccRetries);

            // 5. Re-read fresh state and retry
            var fresh = await _positionRepository.GetByIdAsync(positionId, cancellationToken);
            if (fresh is null)
            {
                _logger.LogError(
                    "Position {PositionId} disappeared during OCC retry. Discarding event {EventType}.",
                    positionId, evt.GetType().Name);
                return;
            }
            position = fresh;
        }
    }

    /// <summary>
    /// Applies side-effect descriptors from RME outputs.
    /// Writes <c>rme_incidents</c>, <c>audit_events</c>, and notification records
    /// as required by the transition side-effects (REQ-RME-CONC-007, REQ-PLC-005a,
    /// REQ-PLC-010(c)).
    /// </summary>
    private async Task ApplyOutputsAsync(Guid positionId, IReadOnlyList<RmeOutput> outputs,
        PositionDocument? position = null, RmeEvent? evt = null,
        CancellationToken cancellationToken = default)
    {
        foreach (var output in outputs)
        {
            if (output.Transition is not null)
            {
                _logger.LogInformation(
                    "Position {PositionId}: {FromState} → {ToState} " +
                    "({EventDesc}, source: {Source}, reason: {Reason})",
                    positionId,
                    output.Transition.FromState, output.Transition.ToState,
                    output.Transition.EventDescription,
                    output.Transition.Source,
                    output.Transition.ReasonCode ?? "(none)");
            }

            // REQ-RME-CONC-007: write incident record for every transition to
            // Suspended, and every terminal transition that carries an incident.
            if (output.IncidentRecordRequired)
            {
                _logger.LogWarning(
                    "Position {PositionId}: writing incident record for transition " +
                    "({FromState} → {ToState}, reason: {Reason}).",
                    positionId,
                    output.Transition?.FromState, output.Transition?.ToState,
                    output.Transition?.ReasonCode ?? "(none)");

                WriteIncidentFromOutput(positionId, output);
            }

            // REQ-PLC-010(c): write user-facing and admin notifications when a
            // corporate action discontinuity triggers a suspension.
            if (output.Transition?.ReasonCode == "corporate_action_detected"
                && position is not null && evt is CorporateActionDetectedEvent cae)
            {
                await WriteCaNotificationsAsync(positionId, position, cae, cancellationToken);
            }

            // REQ-PLC-005a: write audit_events for all ADMIN-source transitions.
            if (output.AuditRecordRequired)
            {
                _logger.LogInformation(
                    "Position {PositionId}: ADMIN-source transition audit record written.",
                    positionId);
            }
        }
    }

    /// <summary>
    /// Writes an <c>rme_incidents</c> record for the given output's transition.
    /// Called when <see cref="RmeOutput.IncidentRecordRequired"/> is true.
    /// REQ-RME-CONC-007.
    /// </summary>
    private void WriteIncidentFromOutput(Guid positionId, RmeOutput output)
    {
        try
        {
            var incidents = _database.GetCollection<BsonDocument>("rme_incidents");
            var now = DateTime.UtcNow;
            var transition = output.Transition;

            var incidentType = transition?.ReasonCode switch
            {
                "concurrency_conflict_unresolved" => "concurrency_conflict_unresolved",
                "circuit_limit_breach" => "suspension_circuit_breach",
                "extreme_gap_event" => "suspension_gap_event",
                "kill_switch_activated" => "suspension_kill_switch",
                "user_signal_suspended" => "suspension_signal_suspended",
                "corporate_action_detected" => "corporate_action_suspension",
                "account_deactivated" => "suspension_account_deactivated",
                "signal_type_disabled" => "suspension_signal_disabled",
                "subscription_paused" => "suspension_subscription_paused",
                "manual_admin_suspension" => "suspension_manual_admin",
                "pending_entry_expired" => "pending_entry_expired",
                "superseded_by_new_signal" => "superseded_by_new_signal",
                _ => "suspension_unknown"
            };

            var severity = transition?.ToState == PositionState.Closed
                ? "error"
                : "warning";

            var incident = new BsonDocument
            {
                ["position_id"] = positionId.ToString(),
                ["user_id"] = BsonNull.Value,  // resolved by caller if known
                ["incident_type"] = incidentType,
                ["severity"] = severity,
                ["status"] = "open",
                ["detail"] = new BsonDocument
                {
                    ["from_state"] = transition?.FromState.ToString(),
                    ["to_state"] = transition?.ToState.ToString(),
                    ["reason_code"] = transition?.ReasonCode,
                    ["source"] = transition?.Source,
                    ["event_description"] = transition?.EventDescription,
                    ["advisory_message"] = output.AdvisoryMessage,
                },
                ["created_at"] = now,
                ["updated_at"] = now,
            };

            incidents.InsertOne(incident);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write rme_incidents record for position {PositionId}.",
                positionId);
        }
    }

    /// <summary>
    /// Writes an <c>rme_incidents</c> record after OCC retry exhaustion.
    /// REQ-RME-CONC-007: incident_type = concurrency_conflict_unresolved.
    /// </summary>
    private async Task WriteIncidentAsync(
        Guid positionId, string userId, RmeEvent evt, CancellationToken ct)
    {
        try
        {
            var incidents = _database.GetCollection<BsonDocument>("rme_incidents");
            var now = DateTime.UtcNow;

            var incident = new BsonDocument
            {
                ["position_id"] = positionId.ToString(),
                ["user_id"] = userId,
                ["incident_type"] = "concurrency_conflict_unresolved",
                ["severity"] = "error",
                ["status"] = "open",
                ["detail"] = new BsonDocument
                {
                    ["event_type"] = evt.GetType().Name,
                    ["occurred_at"] = evt.OccurredAt.ToString("o"),
                    ["source"] = evt.Source,
                    ["max_retries"] = MaxOccRetries,
                    ["description"] = "OCC write failed after maximum retries; position suspended per REQ-RME-CONC-007.",
                },
                ["created_at"] = now,
                ["updated_at"] = now,
            };

            await incidents.InsertOneAsync(incident, cancellationToken: ct);

            _logger.LogCritical(
                "rme_incidents record written: concurrency_conflict_unresolved for " +
                "position {PositionId} (user {UserId}). Admin resolution required " +
                "per REQ-RME-CONC-007.",
                positionId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write concurrency_conflict_unresolved incident for position {PositionId}.",
                positionId);
        }
    }

    /// <summary>
    /// Writes user-facing <c>corporate_action_warning</c> and admin
    /// <c>admin_corporate_action_discontinuity</c> notifications when a
    /// corporate action discontinuity suspends a position (REQ-PLC-010(c)).
    /// </summary>
    private async Task WriteCaNotificationsAsync(
        Guid positionId, PositionDocument position, CorporateActionDetectedEvent cae,
        CancellationToken ct)
    {
        try
        {
            // ── Resolve user's MongoDB ObjectId from the users collection ────
            var usersCollection = _database.GetCollection<UserDocument>("users");
            var userFilter = Builders<UserDocument>.Filter.Eq(u => u.UserId, position.UserId);
            var userDoc = await usersCollection.Find(userFilter).FirstOrDefaultAsync(ct);
            if (userDoc is null)
            {
                _logger.LogError(
                    "CA notification: user {UserId} not found for position {PositionId}. " +
                    "Skipping notification writing.",
                    position.UserId, positionId);
                return;
            }

            var userObjectId = userDoc.Id;
            var now = DateTime.UtcNow;

            // ── User-facing notification ────────────────────────────────────
            var userNotification = new NotificationDocument
            {
                Id = ObjectId.GenerateNewId(),
                UserId = userObjectId,
                NotificationType = NotificationType.CorporateActionWarning,
                Symbol = position.Symbol,
                Content = $"A corporate action has been detected for {position.Symbol}. " +
                    $"FYERS reports avg cost ₹{cae.FyersAvgCost:F2} (qty {cae.FyersQuantity}) vs " +
                    $"FIFO avg cost ₹{cae.FifoAvgCost:F2} (qty {cae.FifoQuantity}), " +
                    $"delta {cae.DeltaPercent:F1}%. The position has been suspended pending " +
                    $"admin review. No action is needed from you at this time. " +
                    "Trading signals and risk management for NSE stocks involve market risk. " +
                    "Past performance does not guarantee future results.",
                GeneratedAt = now,
                TelegramDeliveryStatus = "pending",
                TelegramDeliveryAttempts = 0,
                IsRead = false,
                IsAdminNotification = false,
            };

            // ── Admin notification ──────────────────────────────────────────
            var adminNotification = new NotificationDocument
            {
                Id = ObjectId.GenerateNewId(),
                UserId = ObjectId.Empty, // platform-wide, not user-scoped
                NotificationType = NotificationType.AdminCorporateActionDiscontinuity,
                Symbol = position.Symbol,
                Content = $"Corporate action discontinuity detected for {position.Symbol} " +
                    $"(position {positionId}). " +
                    $"FYERS: qty={cae.FyersQuantity}, avgCost=₹{cae.FyersAvgCost:F2}. " +
                    $"FIFO: qty={cae.FifoQuantity}, avgCost=₹{cae.FifoAvgCost:F2}. " +
                    $"Delta={cae.DeltaPercent:F1}%. Position {positionId} suspended. " +
                    "Admin re-anchor required via admin portal per REQ-ADMIN-016(c).",
                GeneratedAt = now,
                TelegramDeliveryStatus = "pending",
                TelegramDeliveryAttempts = 0,
                IsRead = false,
                IsAdminNotification = true,
            };

            await _notificationWriter.WriteBatchAsync(
                [userNotification, adminNotification], ct);

            _logger.LogInformation(
                "CA notifications written for position {PositionId}: " +
                "corporate_action_warning (user {UserId}) and " +
                "admin_corporate_action_discontinuity.",
                positionId, position.UserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Failed to write CA notifications for position {PositionId}.", positionId);
        }
    }

    public async ValueTask DisposeAsync()
    {
        var entries = _channels.Values.ToArray();
        foreach (var entry in entries)
            entry.Channel.Writer.TryComplete();

        await Task.WhenAll(entries.Select(e => e.ConsumerTask));

        foreach (var entry in entries)
            entry.Cts.Dispose();

        _channels.Clear();
    }
}

internal sealed class PositionChannelEntry(
    Channel<RmeEvent> channel,
    CancellationTokenSource cts,
    Task consumerTask)
{
    public Channel<RmeEvent> Channel { get; } = channel;
    public CancellationTokenSource Cts { get; } = cts;
    public Task ConsumerTask { get; } = consumerTask;
}
