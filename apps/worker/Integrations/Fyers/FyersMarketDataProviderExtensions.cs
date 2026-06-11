using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using SignalStack.MarketData;
using SignalStack.MarketData.Throttling;
using SignalStack.Storage.Universe;

namespace SignalStack.Worker.Integrations.Fyers;

/// <summary>
/// DI registration extensions for the FYERS MDP adapter and throttle.
///
/// Usage in <c>Program.cs</c>:
/// <code>
/// builder.Services.AddFyersMarketDataProvider(builder.Configuration);
/// </code>
///
/// Registers:
/// - A named <see cref="IMarketDataThrottle"/> with FYERS rate limits
///   (keyed service name: <c>"fyers"</c>)
/// - <see cref="FyersMarketDataProvider"/> as <see cref="IMarketDataProvider"/>
/// - A config-based access token provider (dev/test; replace with Key Vault in
///   production via P5-T14/P5-T15)
/// - The FYERS App ID from <c>FYERS_APP_ID</c> env var or config
/// </summary>
public static class FyersMarketDataProviderExtensions
{
    /// <summary>Key used for the FYERS keyed throttle service.</summary>
    public const string ThrottleProviderKey = "fyers";

    /// <summary>
    /// Register the FYERS MDP adapter with throttle layer.
    /// </summary>
    public static IServiceCollection AddFyersMarketDataProvider(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ── Step 1: Register FYERS throttle ───────────────────────────────
        var rateLimitSection = configuration.GetSection("integrations:fyers:rate_limit");
        services.AddMarketDataThrottle(ThrottleProviderKey, rateLimitSection);

        // ── Step 2: Resolve FYERS App ID (required) ───────────────────────
        // Used in the Authorization header as: AppID:{access_token}
        // FYERS v3 does NOT use Bearer scheme — it uses AppID:token format.
        var appId = configuration["FYERS_APP_ID"]
            ?? configuration["integrations:fyers:app_id"]
            ?? throw new InvalidOperationException(
                "FYERS_APP_ID is not configured. Set the FYERS_APP_ID environment variable " +
                "or integrations:fyers:app_id in config.");

        // ── Step 3: Register the admin FYERS access token provider ────────
        // Reads the shared-ingestion admin token from the fyers_tokens MongoDB
        // collection via SharedTokenHealthService. When the token is unavailable,
        // the service transitions to degraded mode (REQ-MARKET-016).
        // Falls back to FYERS_ACCESS_TOKEN env var for dev/test environments.
        services.AddSingleton<Func<Task<string?>>>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<FyersMarketDataProvider>>();
            var healthService = sp.GetRequiredService<SharedTokenHealthService>();
            return () =>
            {
                try
                {
                    return healthService.GetTokenAsync();
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex,
                        "SharedTokenHealthService unavailable. " +
                        "Falling back to config-based FYERS_ACCESS_TOKEN.");
                    var token = configuration["FYERS_ACCESS_TOKEN"]
                        ?? configuration["integrations:fyers:access_token"];
                    return Task.FromResult(token);
                }
            };
        });

        // ── Step 4: Register HttpClient for FYERS API calls ───────────────
        services.AddHttpClient<FyersMarketDataProvider>(client =>
        {
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        // ── Step 5: Register as IMarketDataProvider ────────────────────────
        services.AddSingleton<IMarketDataProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var client = httpClientFactory.CreateClient(nameof(FyersMarketDataProvider));
            var throttle = sp.GetRequiredThrottle(ThrottleProviderKey);
            var tokenProvider = sp.GetRequiredService<Func<Task<string?>>>();
            var logger = sp.GetRequiredService<ILogger<FyersMarketDataProvider>>();

            var provider = new FyersMarketDataProvider(client, throttle, tokenProvider, logger, appId);

            // Wire the invalid-symbol callback: when FYERS returns code -300
            // ("Invalid symbol provided"), flag the symbol in MongoDB so it
            // appears in the admin work queue for review.
            try
            {
                var symbolRepo = sp.GetService<ISymbolMasterRepository>();
                if (symbolRepo is not null)
                {
                    provider.InvalidSymbolCallback = async symbol =>
                    {
                        try
                        {
                            var doc = await symbolRepo.FindBySymbolAsync(symbol);
                            if (doc is not null)
                            {
                                await symbolRepo.MarkFyersInvalidAsync(doc.Id, DateTime.UtcNow);
                                logger.LogWarning(
                                    "Flagged symbol {Symbol} as FYERS-invalid (code -300) " +
                                    "in symbol_master for admin review.",
                                    symbol);
                            }
                            else
                            {
                                logger.LogWarning(
                                    "FYERS returned code -300 for symbol {Symbol}, " +
                                    "but no matching document exists in symbol_master. " +
                                    "Cannot flag for review.",
                                    symbol);
                            }
                        }
                        catch (Exception innerEx)
                        {
                            logger.LogError(innerEx,
                                "Failed to flag symbol {Symbol} as FYERS-invalid " +
                                "in symbol_master.",
                                symbol);
                        }
                    };
                }
                else
                {
                    logger.LogWarning(
                        "ISymbolMasterRepository is not registered. " +
                        "FYERS invalid-symbol detection callback will be a no-op.");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex,
                    "Failed to wire FYERS invalid-symbol callback. " +
                    "Invalid-symbol detection will be a no-op.");
            }

            return provider;
        });

        return services;
    }
}
