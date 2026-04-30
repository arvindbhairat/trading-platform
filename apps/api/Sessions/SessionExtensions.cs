namespace SignalStack.Api.Sessions;

public static class SessionExtensions
{
    public static IServiceCollection AddSessionManagement(this IServiceCollection services)
    {
        services.AddSingleton<ISessionRepository, MongoSessionRepository>();
        return services;
    }
}
