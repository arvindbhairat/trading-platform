using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests for <see cref="PositionDocument.CloneWith"/> — the immutable clone method
/// used by the RME event consumer to produce updated position documents without
/// mutating the original (REQ-RME-CONC-003).
/// </summary>
public sealed class PositionDocumentCloneWithTests
{
    [Fact]
    public void CloneWith_returns_new_instance()
    {
        var original = MakePosition();
        var clone = original.CloneWith();

        Assert.NotSame(original, clone);
    }

    [Fact]
    public void CloneWith_preserves_unchanged_fields()
    {
        var original = MakePosition();
        var clone = original.CloneWith();

        Assert.Equal(original.PositionId, clone.PositionId);
        Assert.Equal(original.UserId, clone.UserId);
        Assert.Equal(original.Symbol, clone.Symbol);
        Assert.Equal(original.EntryPrice, clone.EntryPrice);
        Assert.Equal(original.Quantity, clone.Quantity);
        Assert.Equal(original.Version, clone.Version);
        Assert.Equal(original.CreatedAt, clone.CreatedAt);
    }

    [Fact]
    public void CloneWith_updates_state_when_specified()
    {
        var original = MakePosition();
        Assert.Equal(PositionState.PendingEntry, original.State);

        var clone = original.CloneWith(state: PositionState.Open);

        Assert.Equal(PositionState.Open, clone.State);
        Assert.Equal(PositionState.PendingEntry, original.State); // original unchanged
    }

    [Fact]
    public void CloneWith_sets_ClosedAt_when_transitioning_to_Closed()
    {
        var original = MakePosition();
        Assert.Null(original.ClosedAt);

        var clone = original.CloneWith(state: PositionState.Closed);

        Assert.NotNull(clone.ClosedAt);
    }

    [Fact]
    public void CloneWith_sets_ClosedAt_when_transitioning_to_Rejected()
    {
        var original = MakePosition();
        Assert.Null(original.ClosedAt);

        var clone = original.CloneWith(state: PositionState.Rejected);

        Assert.NotNull(clone.ClosedAt);
    }

    [Fact]
    public void CloneWith_does_not_set_ClosedAt_for_non_terminal_states()
    {
        var original = MakePosition();
        Assert.Null(original.ClosedAt);

        var clone = original.CloneWith(state: PositionState.Open);

        Assert.Null(clone.ClosedAt);
    }

    [Fact]
    public void CloneWith_does_not_set_ClosedAt_for_Suspended()
    {
        var original = MakePosition();

        var clone = original.CloneWith(state: PositionState.Suspended);

        Assert.Null(clone.ClosedAt);
    }

    [Fact]
    public void CloneWith_preserves_ClosedAt_when_not_specified()
    {
        var original = MakePosition();
        original.ClosedAt = DateTime.UtcNow.AddDays(-1);

        var clone = original.CloneWith(); // no state parameter

        Assert.Equal(original.ClosedAt, clone.ClosedAt);
    }

    [Fact]
    public void CloneWith_updates_UpdatedAt()
    {
        var original = MakePosition();
        var before = original.UpdatedAt;

        var clone = original.CloneWith(state: PositionState.Open);

        Assert.True(clone.UpdatedAt >= before);
    }

    [Fact]
    public void CloneWith_can_update_multiple_fields()
    {
        var original = MakePosition();
        var now = DateTime.UtcNow;

        var clone = original.CloneWith(
            state: PositionState.Open,
            currentPrice: 550m,
            stopLoss: 500m,
            trailingStop: 540m);

        Assert.Equal(PositionState.Open, clone.State);
        Assert.Equal(550m, clone.CurrentPrice);
        Assert.Equal(500m, clone.StopLoss);
        Assert.Equal(540m, clone.TrailingStop);
    }

    [Fact]
    public void CloneWith_can_update_advisory_flags()
    {
        var original = MakePosition();

        var clone = original.CloneWith(
            addAdvisoryActive: true,
            reduceAdvisoryActive: true,
            trailingStopActive: true,
            exitAdvisoryActive: true);

        Assert.True(clone.AddAdvisoryActive);
        Assert.True(clone.ReduceAdvisoryActive);
        Assert.True(clone.TrailingStopActive);
        Assert.True(clone.ExitAdvisoryActive);
    }

    [Fact]
    public void CloneWith_can_update_tranche_fields()
    {
        var original = MakePosition();
        var tranches = new List<TrancheRecord>
        {
            new() { EntryPrice = 500m, Quantity = 10m, StopLoss = 480m, EnteredAt = DateTime.UtcNow },
        };

        var clone = original.CloneWith(
            tranches: tranches,
            averageEntryPrice: 500m,
            pyramidEntryCount: 1,
            stopFloor: 480m);

        Assert.Same(tranches, clone.Tranches);
        Assert.Equal(500m, clone.AverageEntryPrice);
        Assert.Equal(1, clone.PyramidEntryCount);
        Assert.Equal(480m, clone.StopFloor);
    }

    [Fact]
    public void CloneWith_can_update_time_stop_fields()
    {
        var original = MakePosition();
        var audit = new List<TimeStopAuditEntry>
        {
            new() { PreviousDate = "2026-05-01", NewDate = "2026-05-05", TriggerEvent = "calendar_edit", RecomputedAt = DateTime.UtcNow },
        };

        var clone = original.CloneWith(
            timeStopDate: "2026-05-05",
            timeStopAudit: audit);

        Assert.Equal("2026-05-05", clone.TimeStopDate);
        Assert.Same(audit, clone.TimeStopAudit);
    }

    [Fact]
    public void CloneWith_can_update_ledger_fence_token()
    {
        var original = MakePosition();
        Assert.Equal(1, original.LedgerFenceToken);

        var clone = original.CloneWith(ledgerFenceToken: 42);

        Assert.Equal(42, clone.LedgerFenceToken);
        Assert.Equal(1, original.LedgerFenceToken);
    }

    private static PositionDocument MakePosition() => new()
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
