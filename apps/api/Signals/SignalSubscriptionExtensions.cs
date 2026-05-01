namespace SignalStack.Api.Signals;

public static class SignalSubscriptionExtensions
{
    public static IServiceCollection AddSignalSubscriptionManagement(
        this IServiceCollection services)
    {
        services.AddSingleton<ISignalSubscriptionRepository, MongoSignalSubscriptionRepository>();
        return services;
    }
}
