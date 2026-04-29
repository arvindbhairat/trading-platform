using System.ComponentModel.DataAnnotations;

namespace SignalStack.Configuration.Ledger;

public sealed class LedgerLockOptions
{
  public const string SectionName = "LedgerLock";

  /// <summary>Lock TTL. Seeded in sys_config as jobs.ledger_lock.ttl_seconds (default 600).</summary>
  [Range(30, 7200)]
  public int TtlSeconds { get; init; } = 600;

  /// <summary>Renewal interval. Seeded in sys_config as jobs.ledger_lock.renewal_interval_seconds (default 60).</summary>
  [Range(10, 600)]
  public int RenewalIntervalSeconds { get; init; } = 60;

  /// <summary>Consecutive-skip count before a warning fires. Seeded in sys_config as jobs.ledger_lock.skip_warn_threshold (default 3).</summary>
  [Range(1, 100)]
  public int SkipWarnThreshold { get; init; } = 3;

  /// <summary>
  /// Redis logical database index for the lock keyspace (ledger:write:lock:*, ledger:write:fence:*,
  /// rme:worker:singleton). Must be configured with maxmemory-policy noeviction per REQ-PORT-031b(c).
  /// Cache keys live in a different index.
  /// </summary>
  [Range(0, 15)]
  public int LockDbIndex { get; init; } = 1;
}
