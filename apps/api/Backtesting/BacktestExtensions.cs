namespace SignalStack.Api.Backtesting;

/// <summary>
/// DI registration for the backtesting subsystem.
/// P4-T3: Backtest engine + SQL Server persistence.
/// P6-T23: RME profile optimisation + REQ-SLO-007 concurrency cap.
/// </summary>
public static class BacktestExtensions
{
    public static IServiceCollection AddBacktestingServices(
        this IServiceCollection services,
        string? sqlConnectionString)
    {
        services.AddSingleton<BacktestEngine>();

        // REQ-SLO-007: concurrent backtest cap (5 platform-wide, 1 per user).
        // Shared by regular backtests and optimisation runs (U-9).
        services.AddSingleton<BacktestConcurrencySemaphore>();

        // P6-T23: RME profile optimisation engine
        services.AddSingleton<ProfileOptimisationEngine>();
        services.AddSingleton<IOptimisationResultRepository, MongoOptimisationResultRepository>();

        if (!string.IsNullOrWhiteSpace(sqlConnectionString))
        {
            services.AddSingleton<IBacktestRepository>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SqlBacktestRepository>>();
                return new SqlBacktestRepository(sqlConnectionString, logger);
            });
        }
        else
        {
            // Fallback: in-memory repository for environments without SQL Server.
            services.AddSingleton<IBacktestRepository, InMemoryBacktestRepository>();
        }

        return services;
    }

    public static RouteGroupBuilder MapBacktestingEndpoints(this RouteGroupBuilder group)
    {
        group.MapBacktestEndpoints();
        group.MapOptimisationEndpoints();
        return group;
    }
}
