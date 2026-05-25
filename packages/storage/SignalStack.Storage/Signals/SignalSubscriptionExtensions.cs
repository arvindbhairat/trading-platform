using Microsoft.Extensions.DependencyInjection;

namespace SignalStack.Storage.Signals;

public static class SignalSubscriptionExtensions
{
    public static IServiceCollection AddSignalSubscriptionManagement(
        this IServiceCollection services)
    {
        services.AddSingleton<ISignalSubscriptionRepository, MongoSignalSubscriptionRepository>();
        return services;
    }
}
