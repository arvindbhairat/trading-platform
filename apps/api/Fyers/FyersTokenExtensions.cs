namespace SignalStack.Api.Fyers;

public static class FyersTokenExtensions
{
    public static IServiceCollection AddFyersTokenManagement(this IServiceCollection services)
    {
        services.AddSingleton<IFyersTokenRepository, MongoFyersTokenRepository>();
        services.AddSingleton<FyersAuthService>();
        return services;
    }
}
