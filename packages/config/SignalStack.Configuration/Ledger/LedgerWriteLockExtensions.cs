using Microsoft.Extensions.DependencyInjection;

namespace SignalStack.Configuration.Ledger;

public static class LedgerWriteLockExtensions
{
  /// <summary>
  /// Registers the per-user trade-ledger write lock primitives (REQ-PORT-031/031a/031b).
  /// Binds <see cref="LedgerLockOptions"/> from the "LedgerLock" configuration section.
  /// Writers are wired in P5-T8.
  /// </summary>
  public static IServiceCollection AddLedgerWriteLock(this IServiceCollection services)
  {
    services
      .AddOptions<LedgerLockOptions>()
      .BindConfiguration(LedgerLockOptions.SectionName)
      .ValidateDataAnnotations()
      .ValidateOnStart();

    services.AddSingleton<ILedgerWriteLock, RedisLedgerWriteLock>();

    return services;
  }
}
