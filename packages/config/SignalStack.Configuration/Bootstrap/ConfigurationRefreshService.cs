using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SignalStack.Configuration.Bootstrap;

public sealed class ConfigurationRefreshService : BackgroundService
{
  private readonly BootstrapConfigurationLoader _loader;
  private readonly SignalStackBootstrapOptions _options;
  private readonly string _contentRootPath;
  private readonly MutableConfigurationProvider _provider;
  private readonly ConfigurationBootstrapState _state;
  private readonly LastKnownGoodCacheStore _cacheStore;
  private readonly ILogger<ConfigurationRefreshService> _logger;

  public ConfigurationRefreshService(
    BootstrapConfigurationLoader loader,
    SignalStackBootstrapOptions options,
    string contentRootPath,
    MutableConfigurationProvider provider,
    ConfigurationBootstrapState state,
    LastKnownGoodCacheStore cacheStore,
    ILogger<ConfigurationRefreshService> logger)
  {
    _loader = loader;
    _options = options;
    _contentRootPath = contentRootPath;
    _provider = provider;
    _state = state;
    _cacheStore = cacheStore;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (_options.LastKnownGood.StalenessAlertThresholdSeconds >= _options.LastKnownGood.MaxAgeSeconds)
    {
      _logger.LogWarning(
        "Bootstrap configuration is misconfigured: staleness alert threshold {StalenessThresholdSeconds}s is not lower than max LKG age {MaxAgeSeconds}s.",
        _options.LastKnownGood.StalenessAlertThresholdSeconds,
        _options.LastKnownGood.MaxAgeSeconds);
    }

    using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(30, _options.AppConfiguration.RefreshIntervalSeconds)));

    while (await timer.WaitForNextTickAsync(stoppingToken))
    {
      var now = DateTimeOffset.UtcNow;

      try
      {
        var values = _loader.LoadRemoteValues(_options, _contentRootPath);
        _provider.Replace(values);
        _cacheStore.Write(values, now);
        _state.MarkSuccessfulRemoteRead(now);
      }
      catch (Exception ex)
      {
        if (_state.MarkRemoteFailure())
        {
          _logger.LogWarning(
            ex,
            "Azure App Configuration was unreachable for {ServiceName}; continuing with the last known configuration snapshot.",
            _state.ServiceName);
        }
      }

      if (_state.ShouldRaiseStalenessAlert(now))
      {
        _logger.LogWarning(
          "config.app_config_unreachable_threshold_exceeded for {ServiceName}: Azure App Configuration has not been reached for {ElapsedSeconds:F0}s.",
          _state.ServiceName,
          _state.GetSecondsSinceLastSuccessfulRead(now));
      }
    }
  }
}
