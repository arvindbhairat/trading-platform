using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;

namespace SignalStack.Worker.Rme;

/// <summary>
/// Immutable transition validator encoding the authoritative state-transition
/// matrix from REQ-PLC-002a. Every permitted state transition is defined as a
/// lookup entry; any source/reason/state combination not in the matrix is
/// rejected. REQ-PLC-003 (terminal-state guard) is enforced at the lookup level:
/// Closed and Rejected have zero outgoing definitions.
/// </summary>
public sealed class TransitionValidator : ITransitionValidator
{
    /// <summary>
    /// Key: (FromState, Source, ReasonCodeOrNull).
    /// Value: the matching row(s). A list is needed because certain Suspended
    /// resolutions (RME_OCC incident clear, CORPORATE_ACTION re-anchor) can
    /// resolve to either Open or PendingEntry depending on the position's
    /// pre-suspension state (REQ-PLC-002a rows 22–23).
    /// </summary>
    private readonly FrozenDictionary<(PositionState, PositionEventSource, string?), PositionTransition[]> _map;

    public TransitionValidator()
    {
        var rows = BuildMatrix();
        _map = rows
            .GroupBy(t => (t.FromState, t.Source, t.ReasonCode))
            .ToFrozenDictionary(g => g.Key, g => g.ToArray());
    }

    /// <inheritdoc />
    public bool TryTransition(
        PositionState currentState,
        PositionEventSource source,
        string? reasonCode,
        [NotNullWhen(true)] out PositionTransition? transition,
        out string? rejectionReason)
    {
        if (IsTerminal(currentState))
        {
            transition = null;
            rejectionReason = $"Position is in terminal state '{currentState}'. "
                + "No outgoing transitions are permitted (REQ-PLC-003).";
            return false;
        }

        if (_map.TryGetValue((currentState, source, reasonCode), out var matches))
        {
            transition = matches[0];
            rejectionReason = null;
            return true;
        }

        // Try without reason code as fallback (some matrix rows have no reason).
        if (reasonCode is not null
            && _map.TryGetValue((currentState, source, null), out var fallback))
        {
            transition = fallback[0];
            rejectionReason = null;
            return true;
        }

        transition = null;
        var reasonFragment = reasonCode is not null
            ? $"reason code '{reasonCode}'"
            : "no reason code";
        rejectionReason = $"Transition from '{currentState}' with source '{source}' "
            + $"and {reasonFragment} is not permitted by the "
            + "authoritative state-transition matrix (REQ-PLC-002a).";
        return false;
    }

    /// <inheritdoc />
    public IReadOnlyList<PositionTransition> GetAllowedTransitions(PositionState fromState)
    {
        return _map
            .Where(kvp => kvp.Key.Item1 == fromState)
            .SelectMany(kvp => kvp.Value)
            .ToArray();
    }

    /// <inheritdoc />
    public bool IsTerminal(PositionState state) =>
        state is PositionState.Closed or PositionState.Rejected;

    /// <summary>
    /// Builds all 27 rows of the authoritative state-transition matrix.
    /// Row numbering matches the table in REQ-PLC-002a.
    /// </summary>
    private static PositionTransition[] BuildMatrix()
    {
        // Row 1:  PendingEntry → Open
        var r01 = Row(PositionState.PendingEntry, PositionState.Open,
            "Entry fill confirmed by LADS",
            PositionEventSource.LADS, TransitionReason.EntryFillConfirmed);

        // Row 2:  PendingEntry → Rejected
        var r02 = Row(PositionState.PendingEntry, PositionState.Rejected,
            "Expiry window elapsed with no fill",
            PositionEventSource.System, TransitionReason.PendingEntryExpired);

        // Row 3:  PendingEntry → Rejected
        var r03 = Row(PositionState.PendingEntry, PositionState.Rejected,
            "Fresh EODSR signal supersedes",
            PositionEventSource.System, TransitionReason.SupersededByNewSignal);

        // Row 4:  PendingEntry → Suspended
        var r04 = Row(PositionState.PendingEntry, PositionState.Suspended,
            "Kill switch activated",
            PositionEventSource.ADMIN, TransitionReason.KillSwitchActivated);

        // Row 5:  PendingEntry → Suspended
        var r05 = Row(PositionState.PendingEntry, PositionState.Suspended,
            "Account deactivated",
            PositionEventSource.ADMIN, TransitionReason.AccountDeactivated);

        // Row 6:  Open → Closed
        var r06 = Row(PositionState.Open, PositionState.Closed,
            "Stop loss breached",
            PositionEventSource.LMDS, TransitionReason.StopLossHit);

        // Row 7:  Open → Closed
        var r07 = Row(PositionState.Open, PositionState.Closed,
            "Trailing stop breached",
            PositionEventSource.LMDS, TransitionReason.TrailingStopHit);

        // Row 8:  Open → Suspended
        var r08 = Row(PositionState.Open, PositionState.Suspended,
            "Circuit limit breach detected",
            PositionEventSource.LMDS, TransitionReason.CircuitLimitBreach);

        // Row 9:  Open → Suspended
        var r09 = Row(PositionState.Open, PositionState.Suspended,
            "Extreme gap-open detected",
            PositionEventSource.LMDS, TransitionReason.ExtremeGapEvent);

        // Row 10: Open → Suspended
        var r10 = Row(PositionState.Open, PositionState.Suspended,
            "OCC retry exhaustion",
            PositionEventSource.RME_OCC, TransitionReason.ConcurrencyConflictUnresolved);

        // Row 11: Open → Suspended
        var r11 = Row(PositionState.Open, PositionState.Suspended,
            "Corporate action discontinuity",
            PositionEventSource.CORPORATE_ACTION, TransitionReason.CorporateActionDetected);

        // Row 12: Open → Suspended
        var r12 = Row(PositionState.Open, PositionState.Suspended,
            "Manual admin suspension",
            PositionEventSource.ADMIN, TransitionReason.ManualAdminSuspension);

        // Row 13: Open → Suspended
        var r13 = Row(PositionState.Open, PositionState.Suspended,
            "Kill switch activated",
            PositionEventSource.ADMIN, TransitionReason.KillSwitchActivated);

        // Row 14: Open → Suspended
        var r14 = Row(PositionState.Open, PositionState.Suspended,
            "Per-user signal suspension",
            PositionEventSource.ADMIN, TransitionReason.UserSignalSuspended);

        // Row 15: Open → Suspended
        var r15 = Row(PositionState.Open, PositionState.Suspended,
            "Account deactivated",
            PositionEventSource.ADMIN, TransitionReason.AccountDeactivated);

        // Row 16: Open → Suspended
        var r16 = Row(PositionState.Open, PositionState.Suspended,
            "Signal type disabled",
            PositionEventSource.ADMIN, TransitionReason.SignalTypeDisabled);

        // Row 17: Open → Suspended
        var r17 = Row(PositionState.Open, PositionState.Suspended,
            "Subscription paused by user",
            PositionEventSource.ADMIN, TransitionReason.SubscriptionPaused);

        // Row 18: Open → Closed
        var r18 = Row(PositionState.Open, PositionState.Closed,
            "User-confirmed full exit via FYERS widget",
            PositionEventSource.User, TransitionReason.ManualExit);

        // Row 19: Open → Closed
        var r19 = Row(PositionState.Open, PositionState.Closed,
            "Admin forced close",
            PositionEventSource.ADMIN, TransitionReason.AdminForcedClose);

        // Row 20: Suspended → Open (circuit clear)
        var r20 = Row(PositionState.Suspended, PositionState.Open,
            "Circuit clear confirmed by LMDS",
            PositionEventSource.LMDS, reasonCode: null);

        // Row 21: Suspended → Open (admin releases ADMIN-sourced suspension)
        var r21 = Row(PositionState.Suspended, PositionState.Open,
            "Admin releases ADMIN-sourced suspension",
            PositionEventSource.ADMIN, reasonCode: null);

        // Row 22a: Suspended → Open (RME_OCC incident clear, position was Open)
        var r22a = Row(PositionState.Suspended, PositionState.Open,
            "Admin clears RME_OCC incident (restore to Open)",
            PositionEventSource.RME_OCC, reasonCode: null);

        // Row 22b: Suspended → PendingEntry (RME_OCC incident clear, position was PendingEntry)
        var r22b = Row(PositionState.Suspended, PositionState.PendingEntry,
            "Admin clears RME_OCC incident (restore to PendingEntry)",
            PositionEventSource.RME_OCC, reasonCode: null);

        // Row 23a: Suspended → Open (CA re-anchor, position was Open)
        var r23a = Row(PositionState.Suspended, PositionState.Open,
            "Admin re-anchors after CA (restore to Open)",
            PositionEventSource.CORPORATE_ACTION, reasonCode: null);

        // Row 23b: Suspended → PendingEntry (CA re-anchor, position was PendingEntry)
        var r23b = Row(PositionState.Suspended, PositionState.PendingEntry,
            "Admin re-anchors after CA (restore to PendingEntry)",
            PositionEventSource.CORPORATE_ACTION, reasonCode: null);

        // Row 24: Suspended → Open (account reactivated)
        var r24 = Row(PositionState.Suspended, PositionState.Open,
            "Account reactivated",
            PositionEventSource.ADMIN, reasonCode: null);

        // Row 25: Suspended → Open (user resumes subscription)
        var r25 = Row(PositionState.Suspended, PositionState.Open,
            "User resumes Subscription",
            PositionEventSource.ADMIN, reasonCode: null);

        // Row 26: Suspended → Open (kill switch deactivated + fresh EODSR)
        var r26 = Row(PositionState.Suspended, PositionState.Open,
            "Kill switch deactivated + fresh EODSR",
            PositionEventSource.ADMIN, reasonCode: null);

        // Row 27: Suspended → Closed (admin forced close from Suspended)
        var r27 = Row(PositionState.Suspended, PositionState.Closed,
            "Admin forced close from Suspended",
            PositionEventSource.ADMIN, TransitionReason.AdminForcedClose);

        return
        [
            r01, r02, r03, r04, r05,
            r06, r07, r08, r09, r10,
            r11, r12, r13, r14, r15,
            r16, r17, r18, r19, r20,
            r21, r22a, r22b, r23a, r23b,
            r24, r25, r26, r27,
        ];
    }

    private static PositionTransition Row(
        PositionState from,
        PositionState to,
        string eventDescription,
        PositionEventSource source,
        string? reasonCode) =>
        new(from, to, eventDescription, source, reasonCode);
}
