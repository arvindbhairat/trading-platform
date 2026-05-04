namespace SignalStack.Api.Admin;

public static class TradingCalendarExtensions
{
    public static IServiceCollection AddTradingCalendarManagement(this IServiceCollection services)
    {
        services.AddSingleton<ITradingCalendarRepository, MongoTradingCalendarRepository>();
        return services;
    }

    /// <summary>
    /// Registers the Time Stop recompute service (REQ-STOP-003a).
    /// Triggered by calendar CRUD endpoints; idempotent singleton.
    /// </summary>
    public static IServiceCollection AddTimeStopRecomputeService(this IServiceCollection services)
    {
        services.AddSingleton<TimeStopRecomputeService>();
        return services;
    }
}
