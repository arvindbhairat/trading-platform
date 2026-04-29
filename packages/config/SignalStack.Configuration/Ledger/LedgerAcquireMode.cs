namespace SignalStack.Configuration.Ledger;

/// <summary>Controls how long AcquireAsync waits for a contested lock. REQ-PORT-031.</summary>
public enum LedgerAcquireMode
{
  /// <summary>Background sync jobs (LADS, on-login, EOD): single attempt, skip user if busy.</summary>
  TryNonBlocking,

  /// <summary>User-initiated manual sync: wait up to 5 seconds.</summary>
  WaitFiveSeconds,

  /// <summary>Admin rebuild / single-symbol backfill: wait up to 30 seconds.</summary>
  WaitThirtySeconds
}
