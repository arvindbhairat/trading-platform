using SignalStack.Configuration.Bootstrap;
using Xunit;

namespace SignalStack.Api.Tests;

public sealed class BootstrapConfigurationLoaderTests
{
  [Fact]
  public void LoadInitialConfiguration_reads_app_config_and_resolves_key_vault_references()
  {
    using var fixture = new BootstrapFixture();

    fixture.WriteRemoteSnapshot(
      """
      {
        "Values": {
          "Telemetry:Otlp:PrimaryEndpoint": "@kv:otlp-primary",
          "FeatureFlags:Example": "true"
        }
      }
      """);

    fixture.WriteKeyVaultSnapshot(
      """
      {
        "otlp-primary": "http://localhost:4317"
      }
      """);

    var loader = new BootstrapConfigurationLoader();
    var result = loader.LoadInitialConfiguration(fixture.CreateOptions(), fixture.RootPath, DateTimeOffset.UtcNow);

    Assert.True(result.AppConfigurationReachable);
    Assert.Equal(ConfigurationSourceKind.AzureAppConfiguration, result.SourceKind);
    Assert.Equal("http://localhost:4317", result.Values["Telemetry:Otlp:PrimaryEndpoint"]);
    Assert.Equal("true", result.Values["FeatureFlags:Example"]);
    Assert.True(File.Exists(fixture.CachePath));
  }

  [Fact]
  public void LoadInitialConfiguration_falls_back_to_fresh_lkg_cache_when_app_config_is_unreachable()
  {
    using var fixture = new BootstrapFixture();

    fixture.WriteRemoteSnapshot(
      """
      {
        "Values": {
          "Telemetry:Otlp:PrimaryEndpoint": "http://localhost:4317"
        }
      }
      """);

    fixture.WriteKeyVaultSnapshot("{}");

    var loader = new BootstrapConfigurationLoader();
    loader.LoadInitialConfiguration(fixture.CreateOptions(), fixture.RootPath, DateTimeOffset.UtcNow.AddMinutes(-30));

    File.Delete(fixture.AppConfigurationPath);

    var fallbackResult = loader.LoadInitialConfiguration(fixture.CreateOptions(), fixture.RootPath, DateTimeOffset.UtcNow);

    Assert.False(fallbackResult.AppConfigurationReachable);
    Assert.Equal(ConfigurationSourceKind.LastKnownGoodCache, fallbackResult.SourceKind);
    Assert.Equal("http://localhost:4317", fallbackResult.Values["Telemetry:Otlp:PrimaryEndpoint"]);
  }

  [Fact]
  public void LoadInitialConfiguration_throws_when_app_config_is_unreachable_and_lkg_cache_is_stale()
  {
    using var fixture = new BootstrapFixture();

    fixture.WriteRemoteSnapshot(
      """
      {
        "Values": {
          "Telemetry:Otlp:PrimaryEndpoint": "http://localhost:4317"
        }
      }
      """);

    fixture.WriteKeyVaultSnapshot("{}");

    var loader = new BootstrapConfigurationLoader();
    loader.LoadInitialConfiguration(
      fixture.CreateOptions(maxAgeSeconds: 1),
      fixture.RootPath,
      DateTimeOffset.UtcNow.AddMinutes(-5));

    File.Delete(fixture.AppConfigurationPath);

    var exception = Assert.Throws<InvalidOperationException>(() =>
      loader.LoadInitialConfiguration(fixture.CreateOptions(maxAgeSeconds: 1), fixture.RootPath, DateTimeOffset.UtcNow));

    Assert.Contains("stale", exception.Message, StringComparison.OrdinalIgnoreCase);
  }

  [Fact]
  public void ConfigurationBootstrapState_raises_staleness_alert_only_once_until_a_successful_refresh()
  {
    var state = new ConfigurationBootstrapState("SignalStack.Api", new LastKnownGoodBootstrapOptions
    {
      MaxAgeSeconds = 86400,
      StalenessAlertThresholdSeconds = 60
    });

    state.Initialize(new ConfigurationBootstrapResult
    {
      Values = new Dictionary<string, string?>(),
      LastSuccessfulReadUtc = DateTimeOffset.UtcNow.AddMinutes(-5),
      SourceKind = ConfigurationSourceKind.LastKnownGoodCache,
      AppConfigurationReachable = false
    });

    Assert.True(state.ShouldRaiseStalenessAlert(DateTimeOffset.UtcNow));
    Assert.False(state.ShouldRaiseStalenessAlert(DateTimeOffset.UtcNow));

    state.MarkSuccessfulRemoteRead(DateTimeOffset.UtcNow);
    state.MarkRemoteFailure();

    Assert.False(state.ShouldRaiseStalenessAlert(DateTimeOffset.UtcNow.AddSeconds(10)));
    Assert.True(state.ShouldRaiseStalenessAlert(DateTimeOffset.UtcNow.AddMinutes(2)));
  }

  private sealed class BootstrapFixture : IDisposable
  {
    public BootstrapFixture()
    {
      RootPath = Path.Combine(Path.GetTempPath(), "signalstack-bootstrap-tests", Guid.NewGuid().ToString("N"));
      Directory.CreateDirectory(RootPath);
      AppConfigurationPath = Path.Combine(RootPath, "appconfig.json");
      KeyVaultPath = Path.Combine(RootPath, "keyvault.json");
      CachePath = Path.Combine(RootPath, "appconfig.lkg.json");
    }

    public string RootPath { get; }
    public string AppConfigurationPath { get; }
    public string KeyVaultPath { get; }
    public string CachePath { get; }

    public void WriteRemoteSnapshot(string json) => File.WriteAllText(AppConfigurationPath, json);

    public void WriteKeyVaultSnapshot(string json) => File.WriteAllText(KeyVaultPath, json);

    public SignalStackBootstrapOptions CreateOptions(int maxAgeSeconds = 86400)
    {
      return new SignalStackBootstrapOptions
      {
        AppConfiguration = new AzureAppConfigurationBootstrapOptions
        {
          SnapshotPath = Path.GetFileName(AppConfigurationPath),
          RefreshIntervalSeconds = 300
        },
        KeyVault = new KeyVaultBootstrapOptions
        {
          SnapshotPath = Path.GetFileName(KeyVaultPath)
        },
        LastKnownGood = new LastKnownGoodBootstrapOptions
        {
          CachePath = CachePath,
          MaxAgeSeconds = maxAgeSeconds,
          StalenessAlertThresholdSeconds = 60
        }
      };
    }

    public void Dispose()
    {
      if (Directory.Exists(RootPath))
      {
        Directory.Delete(RootPath, recursive: true);
      }
    }
  }
}
