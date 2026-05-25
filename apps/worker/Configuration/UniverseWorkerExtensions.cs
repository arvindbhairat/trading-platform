using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SignalStack.Storage.Universe;

namespace Microsoft.Extensions.Hosting;

/// <summary>
/// DI registration extensions for Universe Management services used by the Worker.
/// Registers symbol master, upload, sync health, and symbol probe services.
/// </summary>
public static class UniverseWorkerExtensions
{
    public static IServiceCollection AddUniverseManagement(
        this IServiceCollection services,
        string? sqlConnectionString = null)
    {
        services.AddSingleton<ISymbolMasterRepository, MongoSymbolMasterRepository>();
        services.AddSingleton<IUniverseUploadRepository, MongoUniverseUploadRepository>();

        if (!string.IsNullOrWhiteSpace(sqlConnectionString))
        {
            services.AddSingleton<ISyncHealthRepository>(sp =>
            {
                var logger = sp.GetRequiredService<ILogger<SqlSyncHealthRepository>>();
                return new SqlSyncHealthRepository(sqlConnectionString, logger);
            });
        }
        else
        {
            services.AddSingleton<ISyncHealthRepository>(_ =>
                new NoopSyncHealthRepository());
        }

        services.AddSingleton<ISymbolProbeClient, StubSymbolProbeClient>();
        services.AddSingleton<SymbolProbeService>();

        return services;
    }
}

/// <summary>
/// Fallback sync health repository used when SQL Server connection string is not configured.
/// All symbols are reported as out of sync (no data available).
/// </summary>
internal sealed class NoopSyncHealthRepository : ISyncHealthRepository
{
    public Task<List<SymbolSyncHealth>> GetSyncHealthAsync(
        List<string> suffixes, CancellationToken ct = default)
    {
        var results = suffixes.Select(s => new SymbolSyncHealth
        {
            Suffix = s,
            LastCandleDate = null
        }).ToList();

        return Task.FromResult(results);
    }
}
