namespace SignalStack.Api.Universe;

public static class UniverseExtensions
{
    public static IServiceCollection AddUniverseManagement(
        this IServiceCollection services,
        string? sqlConnectionString = null)
    {
        services.AddSingleton<ISymbolMasterRepository, MongoSymbolMasterRepository>();
        services.AddSingleton<IUniverseUploadRepository, MongoUniverseUploadRepository>();
        services.AddSingleton<UniverseSyncService>();

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
            // Fallback: register a no-op implementation that marks all symbols as out of sync.
            services.AddSingleton<ISyncHealthRepository>(_ =>
                new NoopSyncHealthRepository());
        }

        // Symbol Validity Probe services (REQ-UNIV-021/021a/021b).
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
