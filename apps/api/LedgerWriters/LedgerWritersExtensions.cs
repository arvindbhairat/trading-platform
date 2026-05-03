using SignalStack.Api.LedgerWriters;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// DI registration extensions for trade-ledger writer services.
///
/// Registers:
/// - <see cref="ITradeLedgerRepository"/> → <see cref="MongoTradeLedgerRepository"/>
/// - <see cref="TradeIngestionService"/>
///
/// Call from both API and Worker <c>Program.cs</c>.
/// </summary>
public static class LedgerWritersExtensions
{
    public static IServiceCollection AddTradeLedgerServices(this IServiceCollection services)
    {
        services.AddSingleton<ITradeLedgerRepository, MongoTradeLedgerRepository>();
        services.AddSingleton<TradeIngestionService>();
        return services;
    }
}
