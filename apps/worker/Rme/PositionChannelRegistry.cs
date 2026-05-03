using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
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

    public PositionChannelRegistry(
        ILogger<PositionChannelRegistry> logger,
        IOptions<PositionChannelOptions> options,
        IRmeEventConsumer consumer,
        IPositionRepository positionRepository)
        : this(logger, options, consumer, positionRepository, WorkerTelemetry.Meter) { }

    internal PositionChannelRegistry(
        ILogger<PositionChannelRegistry> logger,
        IOptions<PositionChannelOptions> options,
        IRmeEventConsumer consumer,
        IPositionRepository positionRepository,
        Meter meter)
    {
        _logger = logger;
        _options = options.Value;
        _consumer = consumer;
        _positionRepository = positionRepository;

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
                ApplyOutputs(positionId, outputs);
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
                ApplyOutputs(positionId, outputs);
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
                // TODO: P6-T26 writes rme_incidents record with
                // incident_type: concurrency_conflict_unresolved + admin notification
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
    /// In the P6-T4 baseline, outputs are logged; later tasks wire actual
    /// notification dispatch, rme_events writes, and audit records.
    /// </summary>
    private void ApplyOutputs(Guid positionId, IReadOnlyList<RmeOutput> outputs)
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

            if (output.IncidentRecordRequired)
            {
                _logger.LogWarning(
                    "Position {PositionId}: incident record required (transition to Suspended). " +
                    "Will be wired in P6-T26.", positionId);
            }

            if (output.AuditRecordRequired)
            {
                _logger.LogInformation(
                    "Position {PositionId}: audit record required (ADMIN source). " +
                    "Will be wired in P6-T26.", positionId);
            }
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
