using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Exhaustive contract tests for the authoritative state-transition matrix.
/// At least one test per matrix row (REQ-PLC-002a). Every test asserts:
///   - Transition is permitted (TryTransition returns true)
///   - Target state matches the matrix
///   - Source and reason code match the matrix
///   - Terminal states reject all outgoing transitions (REQ-PLC-003)
/// REQ-PLC-002, REQ-PLC-002a, REQ-PLC-003, REQ-PLC-005a.
/// </summary>
public sealed class TransitionValidatorTests
{
    private static readonly ITransitionValidator V = new TransitionValidator();

    // ─────────────────────────────────────────────────────────────────────────
    // Row 1:  PendingEntry → Open  | LADS | entry_fill_confirmed
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row01_PendingEntry_to_Open_on_LADS_fill()
    {
        var ok = V.TryTransition(
            PositionState.PendingEntry,
            PositionEventSource.LADS,
            TransitionReason.EntryFillConfirmed,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Open, t!.ToState);
        Assert.Equal(PositionEventSource.LADS, t.Source);
        Assert.Equal(TransitionReason.EntryFillConfirmed, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 2:  PendingEntry → Rejected | System | pending_entry_expired
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row02_PendingEntry_to_Rejected_on_expiry()
    {
        var ok = V.TryTransition(
            PositionState.PendingEntry,
            PositionEventSource.System,
            TransitionReason.PendingEntryExpired,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Rejected, t!.ToState);
        Assert.Equal(PositionEventSource.System, t.Source);
        Assert.Equal(TransitionReason.PendingEntryExpired, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 3:  PendingEntry → Rejected | System | superseded_by_new_signal
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row03_PendingEntry_to_Rejected_on_supersede()
    {
        var ok = V.TryTransition(
            PositionState.PendingEntry,
            PositionEventSource.System,
            TransitionReason.SupersededByNewSignal,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Rejected, t!.ToState);
        Assert.Equal(PositionEventSource.System, t.Source);
        Assert.Equal(TransitionReason.SupersededByNewSignal, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 4:  PendingEntry → Suspended | ADMIN | kill_switch_activated
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row04_PendingEntry_to_Suspended_on_kill_switch()
    {
        var ok = V.TryTransition(
            PositionState.PendingEntry,
            PositionEventSource.ADMIN,
            TransitionReason.KillSwitchActivated,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Suspended, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Equal(TransitionReason.KillSwitchActivated, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 5:  PendingEntry → Suspended | ADMIN | account_deactivated
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row05_PendingEntry_to_Suspended_on_account_deactivated()
    {
        var ok = V.TryTransition(
            PositionState.PendingEntry,
            PositionEventSource.ADMIN,
            TransitionReason.AccountDeactivated,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Suspended, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Equal(TransitionReason.AccountDeactivated, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 6:  Open → Closed | LMDS | stop_loss_hit
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row06_Open_to_Closed_on_stop_loss_hit()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.LMDS,
            TransitionReason.StopLossHit,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Closed, t!.ToState);
        Assert.Equal(PositionEventSource.LMDS, t.Source);
        Assert.Equal(TransitionReason.StopLossHit, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 7:  Open → Closed | LMDS | trailing_stop_hit
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row07_Open_to_Closed_on_trailing_stop_hit()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.LMDS,
            TransitionReason.TrailingStopHit,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Closed, t!.ToState);
        Assert.Equal(PositionEventSource.LMDS, t.Source);
        Assert.Equal(TransitionReason.TrailingStopHit, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 8:  Open → Suspended | LMDS | circuit_limit_breach
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row08_Open_to_Suspended_on_circuit_limit_breach()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.LMDS,
            TransitionReason.CircuitLimitBreach,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Suspended, t!.ToState);
        Assert.Equal(PositionEventSource.LMDS, t.Source);
        Assert.Equal(TransitionReason.CircuitLimitBreach, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 9:  Open → Suspended | LMDS | extreme_gap_event
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row09_Open_to_Suspended_on_extreme_gap()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.LMDS,
            TransitionReason.ExtremeGapEvent,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Suspended, t!.ToState);
        Assert.Equal(PositionEventSource.LMDS, t.Source);
        Assert.Equal(TransitionReason.ExtremeGapEvent, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 10: Open → Suspended | RME_OCC | concurrency_conflict_unresolved
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row10_Open_to_Suspended_on_OCC_exhaustion()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.RME_OCC,
            TransitionReason.ConcurrencyConflictUnresolved,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Suspended, t!.ToState);
        Assert.Equal(PositionEventSource.RME_OCC, t.Source);
        Assert.Equal(TransitionReason.ConcurrencyConflictUnresolved, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 11: Open → Suspended | CORPORATE_ACTION | corporate_action_detected
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row11_Open_to_Suspended_on_corporate_action()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.CORPORATE_ACTION,
            TransitionReason.CorporateActionDetected,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Suspended, t!.ToState);
        Assert.Equal(PositionEventSource.CORPORATE_ACTION, t.Source);
        Assert.Equal(TransitionReason.CorporateActionDetected, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 12: Open → Suspended | ADMIN | manual_admin_suspension
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row12_Open_to_Suspended_on_manual_admin_suspension()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.ADMIN,
            TransitionReason.ManualAdminSuspension,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Suspended, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Equal(TransitionReason.ManualAdminSuspension, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 13: Open → Suspended | ADMIN | kill_switch_activated
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row13_Open_to_Suspended_on_kill_switch()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.ADMIN,
            TransitionReason.KillSwitchActivated,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Suspended, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Equal(TransitionReason.KillSwitchActivated, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 14: Open → Suspended | ADMIN | user_signal_suspended
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row14_Open_to_Suspended_on_user_signal_suspended()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.ADMIN,
            TransitionReason.UserSignalSuspended,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Suspended, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Equal(TransitionReason.UserSignalSuspended, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 15: Open → Suspended | ADMIN | account_deactivated
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row15_Open_to_Suspended_on_account_deactivated()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.ADMIN,
            TransitionReason.AccountDeactivated,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Suspended, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Equal(TransitionReason.AccountDeactivated, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 16: Open → Suspended | ADMIN | signal_type_disabled
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row16_Open_to_Suspended_on_signal_type_disabled()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.ADMIN,
            TransitionReason.SignalTypeDisabled,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Suspended, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Equal(TransitionReason.SignalTypeDisabled, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 17: Open → Suspended | ADMIN | subscription_paused
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row17_Open_to_Suspended_on_subscription_paused()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.ADMIN,
            TransitionReason.SubscriptionPaused,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Suspended, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Equal(TransitionReason.SubscriptionPaused, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 18: Open → Closed | User | manual_exit
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row18_Open_to_Closed_on_manual_exit()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.User,
            TransitionReason.ManualExit,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Closed, t!.ToState);
        Assert.Equal(PositionEventSource.User, t.Source);
        Assert.Equal(TransitionReason.ManualExit, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 19: Open → Closed | ADMIN | admin_forced_close
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row19_Open_to_Closed_on_admin_forced_close()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.ADMIN,
            TransitionReason.AdminForcedClose,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Closed, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Equal(TransitionReason.AdminForcedClose, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 20: Suspended → Open | LMDS | (no reason code)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row20_Suspended_to_Open_on_circuit_clear()
    {
        var ok = V.TryTransition(
            PositionState.Suspended,
            PositionEventSource.LMDS,
            reasonCode: null,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Open, t!.ToState);
        Assert.Equal(PositionEventSource.LMDS, t.Source);
        Assert.Null(t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 21: Suspended → Open | ADMIN | (no reason code — admin releases suspension)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row21_Suspended_to_Open_on_admin_release()
    {
        var ok = V.TryTransition(
            PositionState.Suspended,
            PositionEventSource.ADMIN,
            reasonCode: null,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Open, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Null(t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 22a: Suspended → Open | RME_OCC | (no reason — incident clear, was Open)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row22a_Suspended_to_Open_on_OCC_incident_clear()
    {
        var ok = V.TryTransition(
            PositionState.Suspended,
            PositionEventSource.RME_OCC,
            reasonCode: null,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Open, t!.ToState);
        Assert.Equal(PositionEventSource.RME_OCC, t.Source);
        Assert.Null(t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 22b: Suspended → PendingEntry | RME_OCC | (no reason — incident clear, was PendingEntry)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row22b_Suspended_to_PendingEntry_on_OCC_incident_clear()
    {
        // With source=RME_OCC and no reason code, there are two valid targets
        // (Open and PendingEntry). Both must be in the allowed list.
        var transitions = V.GetAllowedTransitions(PositionState.Suspended);
        var occRows = transitions
            .Where(t => t.Source == PositionEventSource.RME_OCC && t.ReasonCode is null)
            .ToArray();

        Assert.Contains(occRows, t => t.ToState == PositionState.PendingEntry);
        Assert.Contains(occRows, t => t.ToState == PositionState.Open);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 23a: Suspended → Open | CORPORATE_ACTION | (no reason — re-anchor, was Open)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row23a_Suspended_to_Open_on_CA_reanchor()
    {
        var ok = V.TryTransition(
            PositionState.Suspended,
            PositionEventSource.CORPORATE_ACTION,
            reasonCode: null,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Open, t!.ToState);
        Assert.Equal(PositionEventSource.CORPORATE_ACTION, t.Source);
        Assert.Null(t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 23b: Suspended → PendingEntry | CORPORATE_ACTION | (no reason — re-anchor, was PendingEntry)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row23b_Suspended_to_PendingEntry_on_CA_reanchor()
    {
        var transitions = V.GetAllowedTransitions(PositionState.Suspended);
        var caRows = transitions
            .Where(t => t.Source == PositionEventSource.CORPORATE_ACTION && t.ReasonCode is null)
            .ToArray();

        Assert.Contains(caRows, t => t.ToState == PositionState.PendingEntry);
        Assert.Contains(caRows, t => t.ToState == PositionState.Open);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 24: Suspended → Open | ADMIN | (no reason — account reactivated)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row24_Suspended_to_Open_on_account_reactivated()
    {
        var ok = V.TryTransition(
            PositionState.Suspended,
            PositionEventSource.ADMIN,
            reasonCode: null,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Open, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Null(t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 25: Suspended → Open | ADMIN | (no reason — subscription resumed)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row25_Suspended_to_Open_on_subscription_resumed()
    {
        var ok = V.TryTransition(
            PositionState.Suspended,
            PositionEventSource.ADMIN,
            reasonCode: null,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Open, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Null(t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 26: Suspended → Open | ADMIN | (no reason — kill switch deactivated + EODSR)
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row26_Suspended_to_Open_on_kill_switch_deactivated()
    {
        var ok = V.TryTransition(
            PositionState.Suspended,
            PositionEventSource.ADMIN,
            reasonCode: null,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Open, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Null(t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Row 27: Suspended → Closed | ADMIN | admin_forced_close
    // ─────────────────────────────────────────────────────────────────────────
    [Fact]
    public void Row27_Suspended_to_Closed_on_admin_forced_close()
    {
        var ok = V.TryTransition(
            PositionState.Suspended,
            PositionEventSource.ADMIN,
            TransitionReason.AdminForcedClose,
            out var t, out _);
        Assert.True(ok);
        Assert.Equal(PositionState.Closed, t!.ToState);
        Assert.Equal(PositionEventSource.ADMIN, t.Source);
        Assert.Equal(TransitionReason.AdminForcedClose, t.ReasonCode);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REQ-PLC-003: Terminal-state guards
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Closed_position_rejects_all_transitions()
    {
        var ok = V.TryTransition(
            PositionState.Closed,
            PositionEventSource.ADMIN,
            TransitionReason.AdminForcedClose,
            out _, out var rejection);
        Assert.False(ok);
        Assert.Contains("terminal state", rejection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejected_position_rejects_all_transitions()
    {
        var ok = V.TryTransition(
            PositionState.Rejected,
            PositionEventSource.System,
            TransitionReason.PendingEntryExpired,
            out _, out var rejection);
        Assert.False(ok);
        Assert.Contains("terminal state", rejection, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(PositionState.Closed)]
    [InlineData(PositionState.Rejected)]
    public void Every_source_rejected_for_terminal_states(PositionState terminal)
    {
        var sources = (PositionEventSource[])Enum.GetValues(typeof(PositionEventSource));
        foreach (var source in sources)
        {
            var ok = V.TryTransition(terminal, source, reasonCode: null, out _, out var rejection);
            Assert.False(ok);
            Assert.Contains("terminal state", rejection, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Invalid transitions — combinations not in the matrix
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void PendingEntry_rejects_non_matrix_source()
    {
        var ok = V.TryTransition(
            PositionState.PendingEntry,
            PositionEventSource.LMDS,
            TransitionReason.StopLossHit,
            out _, out var rejection);
        Assert.False(ok);
        Assert.Contains("not permitted", rejection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_rejects_LADS_source()
    {
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.LADS,
            TransitionReason.EntryFillConfirmed,
            out _, out var rejection);
        Assert.False(ok);
        Assert.Contains("not permitted", rejection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Open_rejects_non_matched_reason_code()
    {
        // System source with reason code that doesn't apply to Open
        var ok = V.TryTransition(
            PositionState.Open,
            PositionEventSource.System,
            TransitionReason.PendingEntryExpired,
            out _, out var rejection);
        Assert.False(ok);
        Assert.Contains("not permitted", rejection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Suspended_rejects_User_source()
    {
        var ok = V.TryTransition(
            PositionState.Suspended,
            PositionEventSource.User,
            TransitionReason.ManualExit,
            out _, out var rejection);
        Assert.False(ok);
        Assert.Contains("not permitted", rejection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Suspended_rejects_LADS_source()
    {
        var ok = V.TryTransition(
            PositionState.Suspended,
            PositionEventSource.LADS,
            TransitionReason.EntryFillConfirmed,
            out _, out var rejection);
        Assert.False(ok);
        Assert.Contains("not permitted", rejection, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Suspended_rejects_System_source()
    {
        var ok = V.TryTransition(
            PositionState.Suspended,
            PositionEventSource.System,
            TransitionReason.PendingEntryExpired,
            out _, out var rejection);
        Assert.False(ok);
        Assert.Contains("not permitted", rejection, StringComparison.OrdinalIgnoreCase);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // IsTerminal helper
    // ─────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(PositionState.PendingEntry, false)]
    [InlineData(PositionState.Open, false)]
    [InlineData(PositionState.Suspended, false)]
    [InlineData(PositionState.Closed, true)]
    [InlineData(PositionState.Rejected, true)]
    public void IsTerminal_returns_expected(PositionState state, bool expected)
    {
        Assert.Equal(expected, V.IsTerminal(state));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Count verification — the matrix has 27 rows in REQ-PLC-002a, but rows 22
    // (RME_OCC incident clear) and 23 (CA re-anchor) each have two possible
    // target states (Open or PendingEntry). The validator expands these into
    // separate definitions, yielding 29 total entries.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Matrix_has_29_transition_definitions()
    {
        var allStates = (PositionState[])Enum.GetValues(typeof(PositionState));
        var total = 0;
        foreach (var state in allStates)
        {
            total += V.GetAllowedTransitions(state).Count;
        }
        Assert.Equal(29, total);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // REQ-PLC-005a: Every Suspended transition must produce incident record
    // verified through the output metadata. Here we verify that the admittable
    // Suspended transitions carry the expected source/reason combinations
    // per the exhaustive list in REQ-PLC-005a.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void All_authorised_Suspended_producers_are_represented()
    {
        // Collect all transitions that go TO Suspended
        var allStates = (PositionState[])Enum.GetValues(typeof(PositionState));
        var toSuspended = new List<PositionTransition>();
        foreach (var from in allStates)
        {
            toSuspended.AddRange(
                V.GetAllowedTransitions(from).Where(t => t.ToState == PositionState.Suspended));
        }

        // REQ-PLC-005a enumerates: LMDS (2 reasons), RME_OCC (1), CORPORATE_ACTION (1), ADMIN (6)
        Assert.Contains(toSuspended, t => t is { Source: PositionEventSource.LMDS, ReasonCode: TransitionReason.CircuitLimitBreach });
        Assert.Contains(toSuspended, t => t is { Source: PositionEventSource.LMDS, ReasonCode: TransitionReason.ExtremeGapEvent });
        Assert.Contains(toSuspended, t => t is { Source: PositionEventSource.RME_OCC, ReasonCode: TransitionReason.ConcurrencyConflictUnresolved });
        Assert.Contains(toSuspended, t => t is { Source: PositionEventSource.CORPORATE_ACTION, ReasonCode: TransitionReason.CorporateActionDetected });
        Assert.Contains(toSuspended, t => t is { Source: PositionEventSource.ADMIN, ReasonCode: TransitionReason.ManualAdminSuspension });
        Assert.Contains(toSuspended, t => t is { Source: PositionEventSource.ADMIN, ReasonCode: TransitionReason.KillSwitchActivated });
        Assert.Contains(toSuspended, t => t is { Source: PositionEventSource.ADMIN, ReasonCode: TransitionReason.UserSignalSuspended });
        Assert.Contains(toSuspended, t => t is { Source: PositionEventSource.ADMIN, ReasonCode: TransitionReason.AccountDeactivated });
        Assert.Contains(toSuspended, t => t is { Source: PositionEventSource.ADMIN, ReasonCode: TransitionReason.SignalTypeDisabled });
        Assert.Contains(toSuspended, t => t is { Source: PositionEventSource.ADMIN, ReasonCode: TransitionReason.SubscriptionPaused });

        // 2 LMDS + 1 RME_OCC + 1 CA + 6 ADMIN = 10 distinct (source, reason) pairs
        // (appearing across both PendingEntry→Suspended and Open→Suspended rows)
        Assert.Equal(10, toSuspended.Select(t => (t.Source, t.ReasonCode)).Distinct().Count());
    }
}
