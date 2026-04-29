namespace SignalStack.Configuration.Ledger;

/// <summary>
/// Increments the <c>ledger_snapshot_version</c> field on the user document in MongoDB as the final
/// durable step of every write path protected by the REQ-PORT-031 lock.
///
/// <para>Invariants (REQ-PORT-031a):</para>
/// <list type="bullet">
///   <item>Call only after all trade-ledger writes have durably committed — never at the start of a
///       write attempt or inside an OCC retry loop.</item>
///   <item>Never call when a fencing-token abort is detected (REQ-PORT-031b invariant A-11).</item>
///   <item>Never call when a lock renewal fails (lock lost).</item>
/// </list>
/// </summary>
public interface ILedgerSnapshotVersionHelper
{
  /// <summary>Atomically increments ledger_snapshot_version for the given user.</summary>
  Task IncrementAsync(string userId, CancellationToken cancellationToken);
}
