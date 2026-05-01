namespace SignalStack.Api.Admin;

public static class TradingCalendarExtensions
{
    public static IServiceCollection AddTradingCalendarManagement(this IServiceCollection services)
    {
        services.AddSingleton<ITradingCalendarRepository, MongoTradingCalendarRepository>();
        return services;
    }
}
