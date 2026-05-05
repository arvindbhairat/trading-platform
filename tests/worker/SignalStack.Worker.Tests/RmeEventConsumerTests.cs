using Microsoft.Extensions.Logging.Abstractions;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Unit tests for <see cref="RmeEventConsumer"/> — the pure-function event processor
/// that maps RME events to position state transitions, advisory flags, and
/// side-effect descriptors (REQ-RME-CONC-003).
///
/// REQ-PLC-002a, REQ-PLC-005a, REQ-RME-CONC-003.
/// </summary>
public sealed class RmeEventConsumerTests
{
    private static readonly ITransitionValidator Validator = new TransitionValidator();

    private static RmeEventConsumer CreateConsumer() =>
        new(Validator, NullLogger<RmeEventConsumer>.Instance);

    // ─────────────────────────────────────────────────────────────────────────────
    // MapSource — unrecognised source produces empty result
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MapSource_returns_null_for_unrecognised_source()
    {
        var evt = new DrawdownStateChangedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "UNKNOWN_SOURCE",
            Level = Rme.DrawdownLevel.Advisory,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.Empty(outputs);
        Assert.Same(pos, updated);
    }

    [Fact]
    public async Task MapSource_recognises_System_source()
    {
        var evt = new DrawdownStateChangedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "System",
            Level = Rme.DrawdownLevel.Advisory,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        // System is a valid source (→ PositionEventSource.System), but DrawdownStateChangedEvent
        // has no MapReason mapping and no advisory flags, so no outputs are produced.
        Assert.Empty(outputs);
        Assert.Same(pos, updated);
    }

    [Fact]
    public async Task MapSource_recognises_RME_OCC_source()
    {
        var evt = new DrawdownStateChangedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "RME_OCC",
            Level = Rme.DrawdownLevel.Advisory,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.Empty(outputs);
        Assert.Same(pos, updated);
    }

    [Fact]
    public async Task MapSource_recognises_User_source()
    {
        var evt = new DrawdownStateChangedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "User",
            Level = Rme.DrawdownLevel.Advisory,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.Empty(outputs);
        Assert.Same(pos, updated);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // MapReason — event-to-reason-code mapping
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MapReason_maps_FillConfirmedEvent_to_entry_fill_confirmed()
    {
        var evt = new FillConfirmedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LADS",
            FilledQty = 10m,
            AvgFillPrice = 500m,
        };
        var pos = MakePendingEntryPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        var transition = Assert.Single(outputs)!.Transition;
        Assert.NotNull(transition);
        Assert.Equal(TransitionReason.EntryFillConfirmed, transition.ReasonCode);
        Assert.Equal(PositionState.Open, updated.State);
    }

    [Theory]
    [InlineData(Rme.LevelType.Stop, TransitionReason.StopLossHit)]
    [InlineData(Rme.LevelType.TrailingStop, TransitionReason.TrailingStopHit)]
    public async Task MapReason_maps_PriceLevelBreached_to_stop_reasons(
        Rme.LevelType level, string expectedReason)
    {
        var evt = new PriceLevelBreachedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LMDS",
            Level = level,
            Price = 2400m,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        var transition = Assert.Single(outputs)!.Transition;
        Assert.NotNull(transition);
        Assert.Equal(expectedReason, transition.ReasonCode);
        Assert.Equal(PositionState.Closed, updated.State);
    }

    [Theory]
    [InlineData(Rme.LevelType.Add)]
    [InlineData(Rme.LevelType.Reduce)]
    public async Task MapReason_returns_null_for_advisory_levels(Rme.LevelType level)
    {
        var evt = new PriceLevelBreachedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LMDS",
            Level = level,
            Price = 2600m,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        var output = Assert.Single(outputs);
        Assert.Null(output.Transition); // advisory-only
        Assert.NotNull(output.AdvisoryFlags);
    }

    [Fact]
    public async Task MapReason_returns_KillSwitchActivated_for_KillSwitchActivatedEvent()
    {
        var evt = new KillSwitchActivatedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "ADMIN",
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        var transition = Assert.Single(outputs)!.Transition;
        Assert.NotNull(transition);
        Assert.Equal(TransitionReason.KillSwitchActivated, transition.ReasonCode);
        Assert.Equal(PositionState.Suspended, updated.State);
    }

    [Fact]
    public async Task MapReason_returns_CorporateActionDetected_for_CorporateActionDetectedEvent()
    {
        var evt = new CorporateActionDetectedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "CORPORATE_ACTION",
            FyersQuantity = 110m,
            FyersAvgCost = 520m,
            FifoQuantity = 100m,
            FifoAvgCost = 500m,
            DeltaPercent = 4.0m,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        var transition = Assert.Single(outputs)!.Transition;
        Assert.NotNull(transition);
        Assert.Equal(TransitionReason.CorporateActionDetected, transition.ReasonCode);
        Assert.Equal(PositionState.Suspended, updated.State);
    }

    [Fact]
    public async Task MapReason_returns_null_for_TrailingStopUpdateEvent()
    {
        var evt = new TrailingStopUpdateEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "EOD_STOP",
            NewSessionClose = 2600m,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.Empty(outputs);
        Assert.Same(pos, updated);
    }

    [Fact]
    public async Task MapReason_returns_null_for_DrawdownStateChangedEvent()
    {
        var evt = new DrawdownStateChangedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LMDS",
            Level = Rme.DrawdownLevel.Breach,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.Empty(outputs);
        Assert.Same(pos, updated);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // MapAdvisoryFlagsUpdate
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MapAdvisoryFlagsUpdate_sets_add_flag_for_Add_level()
    {
        var evt = new PriceLevelBreachedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LMDS",
            Level = Rme.LevelType.Add,
            Price = 2600m,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        var output = Assert.Single(outputs);
        Assert.NotNull(output.AdvisoryFlags);
        Assert.True(output.AdvisoryFlags.AddAdvisoryActive);
        Assert.Null(output.AdvisoryFlags.ReduceAdvisoryActive);
        Assert.Null(output.AdvisoryFlags.TrailingStopActive);
        Assert.Null(output.AdvisoryFlags.ExitAdvisoryActive);
    }

    [Fact]
    public async Task MapAdvisoryFlagsUpdate_sets_reduce_flag_for_Reduce_level()
    {
        var evt = new PriceLevelBreachedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LMDS",
            Level = Rme.LevelType.Reduce,
            Price = 2750m,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        var output = Assert.Single(outputs);
        Assert.NotNull(output.AdvisoryFlags);
        Assert.Null(output.AdvisoryFlags.AddAdvisoryActive);
        Assert.True(output.AdvisoryFlags.ReduceAdvisoryActive);
        Assert.Null(output.AdvisoryFlags.TrailingStopActive);
        Assert.Null(output.AdvisoryFlags.ExitAdvisoryActive);
    }

    [Fact]
    public async Task MapAdvisoryFlagsUpdate_returns_null_for_non_advisory_event()
    {
        var evt = new FillConfirmedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LADS",
            FilledQty = 10m,
            AvgFillPrice = 500m,
        };
        var pos = MakePendingEntryPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        var output = Assert.Single(outputs);
        Assert.NotNull(output.Transition); // state transition happened
        Assert.Null(output.AdvisoryFlags); // no advisory flag change
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // ConsumeAsync — full processing paths
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConsumeAsync_produces_updated_position_and_transition()
    {
        var evt = new FillConfirmedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LADS",
            FilledQty = 10m,
            AvgFillPrice = 500m,
        };
        var pos = MakePendingEntryPosition();
        var consumer = CreateConsumer();

        var (updated, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.NotSame(pos, updated); // must be a new instance (pure function)
        Assert.Equal(PositionState.Open, updated.State);
        Assert.Equal(pos.PositionId, updated.PositionId);
        Assert.Equal(pos.UserId, updated.UserId);
        Assert.Equal(pos.Symbol, updated.Symbol);
        Assert.Equal(pos.Version, updated.Version); // unchanged; OCC is at write time

        var output = Assert.Single(outputs);
        Assert.NotNull(output.Transition);
        Assert.Equal(PositionState.PendingEntry, output.Transition.FromState);
        Assert.Equal(PositionState.Open, output.Transition.ToState);
        Assert.Equal(PositionEventSource.LADS, output.Transition.Source);
        Assert.Equal(TransitionReason.EntryFillConfirmed, output.Transition.ReasonCode);
    }

    [Fact]
    public async Task ConsumeAsync_sets_IncidentRecordRequired_when_transitioning_to_Suspended()
    {
        var evt = new KillSwitchActivatedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "ADMIN",
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (_, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        var output = Assert.Single(outputs);
        Assert.True(output.IncidentRecordRequired);
    }

    [Fact]
    public async Task ConsumeAsync_sets_IncidentRecordRequired_false_when_not_suspending()
    {
        var evt = new FillConfirmedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LADS",
            FilledQty = 10m,
            AvgFillPrice = 500m,
        };
        var pos = MakePendingEntryPosition();
        var consumer = CreateConsumer();

        var (_, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        var output = Assert.Single(outputs);
        Assert.False(output.IncidentRecordRequired);
    }

    [Fact]
    public async Task ConsumeAsync_sets_AuditRecordRequired_for_ADMIN_source()
    {
        var evt = new KillSwitchActivatedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "ADMIN",
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (_, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        var output = Assert.Single(outputs);
        Assert.True(output.AuditRecordRequired);
    }

    [Fact]
    public async Task ConsumeAsync_sets_AuditRecordRequired_false_for_non_ADMIN_source()
    {
        var evt = new FillConfirmedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LADS",
            FilledQty = 10m,
            AvgFillPrice = 500m,
        };
        var pos = MakePendingEntryPosition();
        var consumer = CreateConsumer();

        var (_, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        var output = Assert.Single(outputs);
        Assert.False(output.AuditRecordRequired);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // FormatAdvisoryMessage
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AdvisoryMessage_contains_event_specific_text_for_fill_confirmed()
    {
        var evt = new FillConfirmedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LADS",
            FilledQty = 10m,
            AvgFillPrice = 500m,
        };
        var pos = MakePendingEntryPosition();
        var consumer = CreateConsumer();

        var (_, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.Contains("Entry fill confirmed", outputs[0].AdvisoryMessage);
    }

    [Fact]
    public async Task AdvisoryMessage_contains_stop_loss_for_stop_breach()
    {
        var evt = new PriceLevelBreachedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LMDS",
            Level = Rme.LevelType.Stop,
            Price = 2400m,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (_, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.Contains("Stop loss level breached", outputs[0].AdvisoryMessage);
    }

    [Fact]
    public async Task AdvisoryMessage_contains_trailing_stop_for_trailing_stop_breach()
    {
        var evt = new PriceLevelBreachedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LMDS",
            Level = Rme.LevelType.TrailingStop,
            Price = 2600m,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (_, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.Contains("Trailing stop level breached", outputs[0].AdvisoryMessage);
    }

    [Fact]
    public async Task AdvisoryMessage_contains_add_advisory_for_add_level()
    {
        var evt = new PriceLevelBreachedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LMDS",
            Level = Rme.LevelType.Add,
            Price = 2600m,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (_, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.Contains("Add advisory", outputs[0].AdvisoryMessage);
    }

    [Fact]
    public async Task AdvisoryMessage_contains_reduce_advisory_for_reduce_level()
    {
        var evt = new PriceLevelBreachedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LMDS",
            Level = Rme.LevelType.Reduce,
            Price = 2750m,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (_, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.Contains("Reduce advisory", outputs[0].AdvisoryMessage);
    }

    [Fact]
    public async Task AdvisoryMessage_contains_kill_switch_text_for_kill_switch()
    {
        var evt = new KillSwitchActivatedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "ADMIN",
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (_, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.Contains("Kill switch activated", outputs[0].AdvisoryMessage);
    }

    [Fact]
    public async Task AdvisoryMessage_contains_cost_details_for_corporate_action()
    {
        var evt = new CorporateActionDetectedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "CORPORATE_ACTION",
            FyersQuantity = 110m,
            FyersAvgCost = 520m,
            FifoQuantity = 100m,
            FifoAvgCost = 500m,
            DeltaPercent = 4.0m,
        };
        var pos = MakeOpenPosition();
        var consumer = CreateConsumer();

        var (_, outputs) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.Contains("Corporate action detected", outputs[0].AdvisoryMessage);
        Assert.Contains("₹520.00", outputs[0].AdvisoryMessage);
        Assert.Contains("₹500.00", outputs[0].AdvisoryMessage);
        Assert.Contains("4.0%", outputs[0].AdvisoryMessage);
    }

    // ─────────────────────────────────────────────────────────────────────────────
    // CloneWith — the consumer returns a new instance, not the original
    // ─────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task ConsumeAsync_returns_new_position_instance_not_the_original()
    {
        var evt = new FillConfirmedEvent
        {
            PositionId = Guid.NewGuid(),
            OccurredAt = DateTimeOffset.UtcNow,
            Source = "LADS",
            FilledQty = 10m,
            AvgFillPrice = 500m,
        };
        var pos = MakePendingEntryPosition();
        var consumer = CreateConsumer();

        var (updated, _) = await consumer.ConsumeAsync(evt, pos, default);

        Assert.NotSame(pos, updated); // immutability contract
    }

    // ─── Helpers ─────────────────────────────────────────────────────────────────

    private static PositionDocument MakePendingEntryPosition() => new()
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

    private static PositionDocument MakeOpenPosition() => new()
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
