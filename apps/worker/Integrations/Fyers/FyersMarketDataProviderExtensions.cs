using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MongoDB.Driver;
using SignalStack.MarketData;
using SignalStack.MarketData.Throttling;

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

        // ── Step 2: Register the admin FYERS access token provider ────────
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

        // ── Step 3: Register HttpClient for FYERS API calls ───────────────
        services.AddHttpClient<FyersMarketDataProvider>(client =>
        {
            client.BaseAddress = new Uri(FyersApiEndpoints.BaseUrl);
            client.Timeout = TimeSpan.FromSeconds(30);
            client.DefaultRequestHeaders.Accept.Add(
                new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));
        });

        // ── Step 4: Register as IMarketDataProvider ────────────────────────
        // Note: For P3-T9, we register as the default IMarketDataProvider.
        // For P3-T10 (cross-provider swap), this will become conditional
        // based on market_data.provider.active.
        services.AddSingleton<IMarketDataProvider>(sp =>
        {
            var httpClientFactory = sp.GetRequiredService<IHttpClientFactory>();
            var client = httpClientFactory.CreateClient(nameof(FyersMarketDataProvider));
            var throttle = sp.GetRequiredThrottle(ThrottleProviderKey);
            var tokenProvider = sp.GetRequiredService<Func<Task<string?>>>();
            var logger = sp.GetRequiredService<ILogger<FyersMarketDataProvider>>();
            return new FyersMarketDataProvider(client, throttle, tokenProvider, logger);
        });

        return services;
    }
}
