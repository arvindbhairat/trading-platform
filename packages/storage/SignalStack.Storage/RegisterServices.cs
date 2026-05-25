using SignalStack.Storage.Admin;
using SignalStack.Storage.Fyers;
using SignalStack.Storage.LedgerWriters;
using SignalStack.Storage.Notifications;
using SignalStack.Storage.Signals;
using SignalStack.Storage.SysConfig;

namespace Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Convenience entry point for registering all SignalStack.Storage services.
/// </summary>
public static class StorageRegistration
{
    public static IServiceCollection AddSignalStackStorage(this IServiceCollection services)
    {
        services.AddTradingCalendarManagement();
        services.AddFyersTokenManagement();
        services.AddTradeLedgerServices();
        services.AddNotificationServices();
        services.AddSignalSubscriptionManagement();
        return services;
    }
}
