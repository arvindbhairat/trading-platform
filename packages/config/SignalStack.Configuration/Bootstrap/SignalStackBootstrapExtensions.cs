using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace SignalStack.Configuration.Bootstrap;

public static class SignalStackBootstrapExtensions
{
  public static void AddSignalStackBootstrapConfiguration(this IHostApplicationBuilder builder, string serviceName)
  {
    Bootstrap(builder.Configuration, builder.Services, builder.Environment, serviceName);
  }

  private static void Bootstrap(
    IConfigurationManager configuration,
    IServiceCollection services,
    IHostEnvironment environment,
    string serviceName)
  {
    var options = configuration
      .GetSection(SignalStackBootstrapOptions.SectionName)
      .Get<SignalStackBootstrapOptions>()
      ?? new SignalStackBootstrapOptions();

    options.AppConfiguration.Endpoint = ResolveEnvironmentOverride("APPCONFIG_ENDPOINT", options.AppConfiguration.Endpoint);
    options.AppConfiguration.SnapshotPath = ResolveEnvironmentOverride("APPCONFIG_SNAPSHOT_PATH", options.AppConfiguration.SnapshotPath);
    options.KeyVault.VaultUri = ResolveEnvironmentOverride("KEYVAULT_URI", options.KeyVault.VaultUri);
    options.KeyVault.SnapshotPath = ResolveEnvironmentOverride("KEYVAULT_SNAPSHOT_PATH", options.KeyVault.SnapshotPath);
    options.LastKnownGood.CachePath = ResolveEnvironmentOverride("APPCONFIG_LKG_CACHE_PATH", options.LastKnownGood.CachePath);

    var loader = new BootstrapConfigurationLoader();
    var now = DateTimeOffset.UtcNow;
    var result = loader.LoadInitialConfiguration(options, environment.ContentRootPath, now);
    var source = new MutableConfigurationSource(result.Values);

    ((IConfigurationBuilder)configuration).Add(source);

    var state = new ConfigurationBootstrapState(serviceName, options.LastKnownGood);
    state.Initialize(result);
    ConfigurationBootstrapTelemetry.RegisterMetrics(state);

    services.AddSingleton(options);
    services.AddSingleton(loader);
    services.AddSingleton(state);
    services.AddSingleton(new LastKnownGoodCacheStore(options.LastKnownGood.CachePath));
    services.AddSingleton(source.Provider);
    services.AddSingleton<IHostedService>(serviceProvider =>
      new ConfigurationRefreshService(
        serviceProvider.GetRequiredService<BootstrapConfigurationLoader>(),
        serviceProvider.GetRequiredService<SignalStackBootstrapOptions>(),
        environment.ContentRootPath,
        serviceProvider.GetRequiredService<MutableConfigurationProvider>(),
        serviceProvider.GetRequiredService<ConfigurationBootstrapState>(),
        serviceProvider.GetRequiredService<LastKnownGoodCacheStore>(),
        serviceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ConfigurationRefreshService>>()));
  }

  private static string ResolveEnvironmentOverride(string variableName, string currentValue)
  {
    var overrideValue = Environment.GetEnvironmentVariable(variableName);
    return string.IsNullOrWhiteSpace(overrideValue) ? currentValue : overrideValue;
  }
}
