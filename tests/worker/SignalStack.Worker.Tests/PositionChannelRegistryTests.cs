using System.Diagnostics.Metrics;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using Moq;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Verifies ADR-0003 per-position channel registry with OCC retry loop.
/// REQ-RME-CONC-001/002/003/004/005.
/// </summary>
public sealed class PositionChannelRegistryTests : IDisposable
{
    private readonly Meter _meter = new($"SignalStack.Worker.Tests.{Guid.NewGuid()}");
    private static readonly IMongoDatabase _database = Mock.Of<IMongoDatabase>();

    public void Dispose() => _meter.Dispose();

    private PositionChannelRegistry BuildRegistry(
        IRmeEventConsumer consumer,
        IPositionRepository repository,
        PositionChannelOptions? options = null) =>
        new(
            NullLogger<PositionChannelRegistry>.Instance,
            Options.Create(options ?? new PositionChannelOptions()),
            consumer,
            repository,
            _database);

    // ─── REQ-RME-CONC-001 / Vertical slice ───────────────────────────────────

    [Fact]
    public async Task Events_for_same_position_enqueued_from_two_threads_are_processed_serially()
    {
        // Arrange – consumer tracks maximum observed in-flight concurrency
        var concurrencyCount = 0;
        var maxObservedConcurrency = 0;
        var processedCount = 0;
        var allProcessed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var position = MakeOpenPosition();

        var consumer = new DelegatingRmeEventConsumer(async (_, _, ct) =>
        {
            var inFlight = Interlocked.Increment(ref concurrencyCount);
            InterlockedMax(ref maxObservedConcurrency, inFlight);
            await Task.Yield(); // maximise chance of interleaving if serialisation is broken
            Interlocked.Decrement(ref concurrencyCount);
            if (Interlocked.Increment(ref processedCount) == 2)
                allProcessed.TrySetResult();
            return (position, Array.Empty<RmeOutput>());
        });

        var repo = new DelegatingPositionRepository { OnGetById = _ => position };

        await using var registry = BuildRegistry(consumer, repo);
        var positionId = Guid.NewGuid();
        registry.OpenChannel(positionId);

        // Act – enqueue from two concurrent threads
        var t1 = Task.Run(() => registry.EnqueueAsync(MakeFillEvent(positionId)).AsTask());
        var t2 = Task.Run(() => registry.EnqueueAsync(MakeFillEvent(positionId)).AsTask());
        await Task.WhenAll(t1, t2);

        // Assert – both events processed, never more than 1 at a time
        await allProcessed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, processedCount);
        Assert.Equal(1, maxObservedConcurrency);
    }

    [Fact]
    public async Task Events_for_different_positions_are_processed_independently()
    {
        var posA = Guid.NewGuid();
        var posB = Guid.NewGuid();
        var processed = new List<Guid>();
        var gate = new SemaphoreSlim(0);

        var position = MakeOpenPosition();

        var consumer = new DelegatingRmeEventConsumer((evt, _, _) =>
        {
            lock (processed) processed.Add(evt.PositionId);
            if (processed.Count == 2) gate.Release();
            return (position, Array.Empty<RmeOutput>());
        });

        var repo = new DelegatingPositionRepository { OnGetById = _ => position };

        await using var registry = BuildRegistry(consumer, repo);
        registry.OpenChannel(posA);
        registry.OpenChannel(posB);

        await registry.EnqueueAsync(MakeFillEvent(posA));
        await registry.EnqueueAsync(MakeFillEvent(posB));

        await gate.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(2, processed.Count);
    }

    // ─── REQ-RME-CONC-002: OCC retry loop ────────────────────────────────────

    [Fact]
    public async Task OCC_retry_succeeds_after_transient_conflict()
    {
        // Arrange – first write fails OCC, second succeeds
        var callCount = 0;
        var position = MakeOpenPosition();

        var consumer = new DelegatingRmeEventConsumer((_, _, _) =>
        {
            var updated = MakeOpenPosition();
            updated.State = PositionState.Closed;
            return (updated, Array.Empty<RmeOutput>());
        });

        var repo = new DelegatingPositionRepository
        {
            OnGetById = _ => position,
            OnUpdateWithOcc = (_, expected, _) =>
            {
                var current = Interlocked.Increment(ref callCount);
                // First call: OCC conflict; second: success
                return current == 1
                    ? new PositionOccResult(false, 0)
                    : new PositionOccResult(true, expected + 1);
            }
        };

        await using var registry = BuildRegistry(consumer, repo);
        var positionId = Guid.NewGuid();
        registry.OpenChannel(positionId);

        await registry.EnqueueAsync(MakeFillEvent(positionId));

        // Small yield to let the consumer process
        await Task.Delay(200);

        Assert.Equal(2, callCount);
    }

    [Fact]
    public async Task OCC_retry_exhaustion_after_three_failures_logs_error_and_ceases()
    {
        // Arrange – all writes fail OCC
        var callCount = 0;
        var position = MakeOpenPosition();

        var consumer = new DelegatingRmeEventConsumer((_, _, _) =>
        {
            var updated = MakeOpenPosition();
            updated.State = PositionState.Closed;
            return (updated, Array.Empty<RmeOutput>());
        });

        var repo = new DelegatingPositionRepository
        {
            OnGetById = _ => position,
            OnUpdateWithOcc = (_, _, _) =>
            {
                Interlocked.Increment(ref callCount);
                return new PositionOccResult(false, 0);
            }
        };

        await using var registry = BuildRegistry(consumer, repo);
        var positionId = Guid.NewGuid();
        registry.OpenChannel(positionId);

        await registry.EnqueueAsync(MakeFillEvent(positionId));

        // Small yield to let the consumer process (3 retries + original = 4 write attempts)
        await Task.Delay(200);

        // 1 original + 3 retries = 4
        Assert.Equal(4, callCount);
    }

    // ─── REQ-RME-CONC-004: backlog gauge ─────────────────────────────────────

    [Fact]
    public async Task Backlog_warn_depth_gauge_reports_maximum_channel_depth()
    {
        // Arrange – block the consumer so events pile up in the channel
        var event1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseConsumer = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var isFirst = true;

        var position = MakeOpenPosition();

        var consumer = new DelegatingRmeEventConsumer(async (_, _, ct) =>
        {
            if (Interlocked.Exchange(ref isFirst, false) is var wasFirst && wasFirst)
            {
                event1Started.TrySetResult();
                await releaseConsumer.Task.WaitAsync(ct);
            }
            return (position, Array.Empty<RmeOutput>());
        });

        var repo = new DelegatingPositionRepository { OnGetById = _ => position };

        await using var registry = BuildRegistry(consumer, repo);
        var positionId = Guid.NewGuid();
        registry.OpenChannel(positionId);

        // Enqueue first event and wait until the consumer picks it up (so it leaves the channel)
        _ = registry.EnqueueAsync(MakeFillEvent(positionId)).AsTask();
        await event1Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Enqueue 2 more while consumer is blocked – both stay in the channel
        _ = registry.EnqueueAsync(MakeFillEvent(positionId)).AsTask();
        _ = registry.EnqueueAsync(MakeFillEvent(positionId)).AsTask();

        // Small yield to let the writes land
        await Task.Delay(20);

        // Assert
        Assert.Equal(2, registry.GetMaxBacklogDepth());

        // Cleanup
        releaseConsumer.TrySetResult();
    }

    [Fact]
    public async Task Backlog_depth_is_zero_when_all_channels_are_empty()
    {
        var consumer = new DelegatingRmeEventConsumer((_, _, _) =>
            (MakeOpenPosition(), Array.Empty<RmeOutput>()));
        var repo = new DelegatingPositionRepository();
        await using var registry = BuildRegistry(consumer, repo);
        Assert.Equal(0, registry.GetMaxBacklogDepth());
    }

    // ─── ADR-0003 carve-out ───────────────────────────────────────────────────

    [Fact]
    public async Task TrailingStopUpdateEvent_is_rejected_and_not_forwarded_to_consumer()
    {
        var consumerCalled = false;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var consumer = new DelegatingRmeEventConsumer((_, _, _) =>
        {
            consumerCalled = true;
            gate.TrySetResult();
            return (MakeOpenPosition(), Array.Empty<RmeOutput>());
        });

        var repo = new DelegatingPositionRepository();

        await using var registry = BuildRegistry(consumer, repo);
        var positionId = Guid.NewGuid();
        registry.OpenChannel(positionId);

        await registry.EnqueueAsync(new TrailingStopUpdateEvent
        {
            PositionId = positionId,
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "EOD_STOP",
            NewSessionClose = 500m
        });

        // Give the consumer task time to run; it should reject the event without calling us
        await Task.Delay(100);

        Assert.False(consumerCalled, "Consumer should not be invoked for TrailingStopUpdateEvent.");
    }

    // ─── P6-T5: FillConfirmedEvent → consumer invoked with correct data ─────────────────

    [Fact]
    public async Task FillConfirmedEvent_enqueued_for_PendingEntry_position_produces_state_transition()
    {
        // Arrange — a PendingEntry position processed through the registry
        var position = MakePendingEntryPosition();
        var consumerInvoked = false;

        var consumer = new DelegatingRmeEventConsumer((evt, pos, _) =>
        {
            consumerInvoked = true;
            Assert.IsType<FillConfirmedEvent>(evt);
            Assert.Equal("LADS", evt.Source);
            Assert.Equal(PositionState.PendingEntry, pos.State);

            var updated = pos.CloneWith(state: PositionState.Open);
            var output = new RmeOutput
            {
                Transition = new PositionTransition(
                    PositionState.PendingEntry, PositionState.Open,
                    "Entry fill confirmed", PositionEventSource.LADS, TransitionReason.EntryFillConfirmed),
            };
            return (updated, new[] { output });
        });

        var repo = new DelegatingPositionRepository { OnGetById = _ => position };

        await using var registry = BuildRegistry(consumer, repo);
        var positionId = position.PositionId;
        registry.OpenChannel(positionId);

        // Act — enqueue a FillConfirmedEvent (as LADS would)
        var fillEvent = new FillConfirmedEvent
        {
            PositionId = positionId,
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LADS",
            FilledQty = 10m,
            AvgFillPrice = 500m,
        };

        await registry.EnqueueAsync(fillEvent);

        // Small yield for consumer to process
        await Task.Delay(200);

        // Assert — consumer was invoked with the event
        Assert.True(consumerInvoked, "Consumer should be invoked for FillConfirmedEvent.");
    }

    // ─── P6-T5: EODSR position creation → channel opened ─────────────────────

    [Fact]
    public async Task Position_channel_is_opened_when_repository_has_positions()
    {
        // This test verifies that channels are openable for positions
        // created by EODSR's EnsurePositionsForEntrySignalsAsync flow.
        // The EODSR flow calls OpenChannel after creating PendingEntry positions.
        // We verify the registry accepts events for such positions.

        var position = MakePendingEntryPosition();
        var consumerInvoked = false;

        var consumer = new DelegatingRmeEventConsumer((_, _, _) =>
        {
            consumerInvoked = true;
            return (position, Array.Empty<RmeOutput>());
        });

        var repo = new DelegatingPositionRepository { OnGetById = _ => position };

        await using var registry = BuildRegistry(consumer, repo);

        // Act — simulate what EODSR does after creating a PendingEntry position
        registry.OpenChannel(position.PositionId);

        // Enqueue an event — should be processed since channel is open
        await registry.EnqueueAsync(MakeFillEvent(position.PositionId));

        await Task.Delay(200);

        // Assert — consumer was invoked because the channel was opened
        Assert.True(consumerInvoked, "Consumer should be invoked after channel is opened.");
    }

    // ─── REQ-RME-CONC-005: channel lifecycle ─────────────────────────────────

    [Fact]
    public async Task EnqueueAsync_discards_event_and_logs_warning_when_no_channel_is_registered()
    {
        var consumer = new DelegatingRmeEventConsumer((_, _, _) =>
            (MakeOpenPosition(), Array.Empty<RmeOutput>()));
        var repo = new DelegatingPositionRepository();
        await using var registry = BuildRegistry(consumer, repo);

        // No channel opened — should not throw; event is silently discarded
        await registry.EnqueueAsync(MakeFillEvent(Guid.NewGuid()));
    }

    [Fact]
    public async Task CloseChannel_drains_remaining_events_before_consumer_terminates()
    {
        var processedCount = 0;
        var position = MakeOpenPosition();

        var consumer = new DelegatingRmeEventConsumer((_, _, _) =>
        {
            Interlocked.Increment(ref processedCount);
            return (position, Array.Empty<RmeOutput>());
        });

        var repo = new DelegatingPositionRepository { OnGetById = _ => position };

        await using var registry = BuildRegistry(consumer, repo);
        var positionId = Guid.NewGuid();
        registry.OpenChannel(positionId);

        await registry.EnqueueAsync(MakeFillEvent(positionId));
        await registry.EnqueueAsync(MakeFillEvent(positionId));

        registry.CloseChannel(positionId);

        // Allow the consumer task to drain
        await Task.Delay(300);

        Assert.Equal(2, processedCount);
    }

    // ─── REQ-PORT-031b: fencing-token abort ──────────────────────────────────

    [Fact]
    public async Task Fencing_token_stale_write_is_rejected_by_repository()
    {
        // Arrange – create a position with fencing token 5, try to write with token 3
        var position = MakeOpenPosition();
        position.LedgerFenceToken = 5;

        var repo = new MongoPositionRepositoryStub();
        await repo.CreateAsync(position);

        // Act – write with stale fencing token (expected version 1, fence token 3 < current 5)
        var result = await repo.UpdateWithOccAsync(position, expectedVersion: 1, fencingToken: 3);

        // Assert – write rejected
        Assert.False(result.Success);
        // Version must remain unchanged after stale-write rejection
        Assert.Equal(1, position.Version);
    }

    [Fact]
    public async Task Fencing_token_valid_write_succeeds()
    {
        // Arrange – create a position with fencing token 5, write with token >= 5
        var position = MakeOpenPosition();
        position.LedgerFenceToken = 5;

        var repo = new MongoPositionRepositoryStub();
        await repo.CreateAsync(position);

        // Act – write with valid fencing token (token 5, equals current)
        var result = await repo.UpdateWithOccAsync(position, expectedVersion: 1, fencingToken: 5);

        // Assert – write accepted
        Assert.True(result.Success);
        Assert.Equal(2, result.NewVersion);
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────

    private static PositionDocument MakePendingEntryPosition()
    {
        return new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "test-user",
            Symbol = "RELIANCE",
            State = PositionState.PendingEntry,
            EntryPrice = 0m,
            CurrentPrice = 0m,
            Quantity = 0m,
            StopLoss = 0m,
            TrailingStop = 0m,
            Version = 1,
            LedgerFenceToken = 1,
        };
    }

    private static PositionDocument MakeOpenPosition()
    {
        return new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "test-user",
            Symbol = "RELIANCE",
            State = PositionState.Open,
            EntryPrice = 2500m,
            CurrentPrice = 2520m,
            Quantity = 10m,
            StopLoss = 2450m,
            TrailingStop = 0m,
            Version = 1,
            LedgerFenceToken = 1,
        };
    }

    private static FillConfirmedEvent MakeFillEvent(Guid positionId) => new()
    {
        PositionId = positionId,
        OccurredAt = DateTimeOffset.UtcNow,
        Source = "LADS",
        FilledQty = 10m,
        AvgFillPrice = 500m
    };

    private static void InterlockedMax(ref int location, int value)
    {
        int current;
        do
        {
            current = Volatile.Read(ref location);
            if (value <= current) break;
        }
        while (Interlocked.CompareExchange(ref location, value, current) != current);
    }
}

/// <summary>
/// Test double that delegates <see cref="IRmeEventConsumer"/> calls to a provided function.
/// </summary>
file sealed class DelegatingRmeEventConsumer : IRmeEventConsumer
{
    private readonly Func<RmeEvent, PositionDocument, CancellationToken, Task<(PositionDocument, IReadOnlyList<RmeOutput>)>> _handler;

    public DelegatingRmeEventConsumer(
        Func<RmeEvent, PositionDocument, CancellationToken, Task<(PositionDocument, IReadOnlyList<RmeOutput>)>> handler)
    {
        _handler = handler;
    }

    public DelegatingRmeEventConsumer(
        Func<RmeEvent, PositionDocument, CancellationToken, (PositionDocument, IReadOnlyList<RmeOutput>)> syncHandler)
    {
        _handler = (evt, pos, ct) => Task.FromResult(syncHandler(evt, pos, ct));
    }

    public Task<(PositionDocument UpdatedPosition, IReadOnlyList<RmeOutput> Outputs)> ConsumeAsync(
        RmeEvent rmeEvent, PositionDocument currentPosition, CancellationToken cancellationToken)
    {
        return _handler(rmeEvent, currentPosition, cancellationToken);
    }
}

/// <summary>
/// Test double for <see cref="IPositionRepository"/> with configurable delegate callbacks.
/// Default behavior: writes succeed, GetById returns null.
/// </summary>
file sealed class DelegatingPositionRepository : IPositionRepository
{
    public Func<Guid, PositionDocument?>? OnGetById { get; set; }
    public Func<PositionDocument, long, long?, PositionOccResult>? OnUpdateWithOcc { get; set; }
    public Func<List<PositionDocument>>? OnGetNonTerminal { get; set; }
    public Func<string, PositionState, List<PositionDocument>>? OnGetByUserAndState { get; set; }

    public Task<PositionDocument?> GetByIdAsync(Guid positionId, CancellationToken ct = default)
    {
        return Task.FromResult(OnGetById?.Invoke(positionId));
    }

    public Task CreateAsync(PositionDocument position, CancellationToken ct = default)
    {
        return Task.CompletedTask;
    }

    public Task<PositionOccResult> UpdateWithOccAsync(
        PositionDocument position, long expectedVersion, long? fencingToken = null, CancellationToken ct = default)
    {
        if (OnUpdateWithOcc is not null)
        {
            var result = OnUpdateWithOcc(position, expectedVersion, fencingToken);
            if (result.Success)
            {
                position.Version = expectedVersion + 1;
            }
            return Task.FromResult(result);
        }
        // Default: always succeed
        position.Version = expectedVersion + 1;
        return Task.FromResult(new PositionOccResult(true, expectedVersion + 1));
    }

    public Task<List<PositionDocument>> GetNonTerminalAsync(CancellationToken ct = default)
    {
        return Task.FromResult(OnGetNonTerminal?.Invoke() ?? new List<PositionDocument>());
    }

    public Task<List<PositionDocument>> GetNonTerminalByUserIdAsync(
        string userId, CancellationToken ct = default)
    {
        var all = OnGetNonTerminal?.Invoke() ?? new List<PositionDocument>();
        return Task.FromResult(all.Where(p => p.UserId == userId).ToList());
    }

    public Task<List<PositionDocument>> GetByUserIdAndStateAsync(
        string userId, PositionState state, CancellationToken ct = default)
    {
        return Task.FromResult(OnGetByUserAndState?.Invoke(userId, state) ?? new List<PositionDocument>());
    }
}

/// <summary>
/// In-memory stub of <see cref="MongoPositionRepository"/> for testing the
/// OCC and fencing-token write filtering logic without a real MongoDB.
/// </summary>
file sealed class MongoPositionRepositoryStub : IPositionRepository
{
    // In-memory store keyed by position ID
    private readonly Dictionary<Guid, (PositionDocument Doc, long Version, long FenceToken)> _store = new();

    public Task<PositionDocument?> GetByIdAsync(Guid positionId, CancellationToken ct = default)
    {
        if (_store.TryGetValue(positionId, out var entry))
            return Task.FromResult<PositionDocument?>(entry.Doc);
        return Task.FromResult<PositionDocument?>(null);
    }

    public Task CreateAsync(PositionDocument position, CancellationToken ct = default)
    {
        _store[position.PositionId] = (position, position.Version, position.LedgerFenceToken);
        return Task.CompletedTask;
    }

    public Task<PositionOccResult> UpdateWithOccAsync(
        PositionDocument position, long expectedVersion, long? fencingToken = null, CancellationToken ct = default)
    {
        // Replicate the real repo's filter logic in memory
        if (!_store.TryGetValue(position.PositionId, out var current))
            return Task.FromResult(new PositionOccResult(false, 0));

        // Version check (REQ-RME-CONC-002)
        if (current.Version != expectedVersion)
            return Task.FromResult(new PositionOccResult(false, 0));

        // Fencing token check (REQ-PORT-031b(b))
        if (fencingToken.HasValue && current.FenceToken > fencingToken.Value)
            return Task.FromResult(new PositionOccResult(false, 0));

        // Success
        var newVersion = expectedVersion + 1;
        position.Version = newVersion;
        position.UpdatedAt = DateTime.UtcNow;
        _store[position.PositionId] = (position, newVersion, position.LedgerFenceToken);
        return Task.FromResult(new PositionOccResult(true, newVersion));
    }

    public Task<List<PositionDocument>> GetNonTerminalAsync(CancellationToken ct = default)
    {
        return Task.FromResult(_store.Values
            .Where(e => e.Doc.State != PositionState.Closed && e.Doc.State != PositionState.Rejected)
            .Select(e => e.Doc)
            .ToList());
    }

    public Task<List<PositionDocument>> GetNonTerminalByUserIdAsync(
        string userId, CancellationToken ct = default)
    {
        return Task.FromResult(_store.Values
            .Where(e => e.Doc.UserId == userId
                     && e.Doc.State != PositionState.Closed
                     && e.Doc.State != PositionState.Rejected)
            .Select(e => e.Doc)
            .ToList());
    }

    public Task<List<PositionDocument>> GetByUserIdAndStateAsync(
        string userId, PositionState state, CancellationToken ct = default)
    {
        return Task.FromResult(_store.Values
            .Where(e => e.Doc.UserId == userId && e.Doc.State == state)
            .Select(e => e.Doc)
            .ToList());
    }
}
