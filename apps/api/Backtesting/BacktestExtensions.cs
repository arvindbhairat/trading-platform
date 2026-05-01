namespace SignalStack.Api.Backtesting;

/// <summary>
/// DI registration for the backtesting subsystem.
/// P4-T3: Backtest engine + SQL Server persistence.
/// </summary>
public static class BacktestExtensions
{
    public static IServiceCollection AddBacktestingServices(
        this IServiceCollection services,
        string? sqlConnectionString)
    {
        services.AddSingleton<BacktestEngine>();

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
        return group.MapBacktestEndpoints();
    }
}
