using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SignalStack.Configuration.Bootstrap;
using Xunit;

namespace SignalStack.Worker.Tests;

public sealed class BootstrapConfigurationExtensionsTests
{
  [Fact]
  public void AddSignalStackBootstrapConfiguration_registers_state_and_loads_remote_values()
  {
    using var fixture = new BootstrapFixture();

    fixture.WriteRemoteSnapshot(
      """
      {
        "Values": {
          "Telemetry:Otlp:PrimaryEndpoint": "@kv:otlp-primary",
          "WorkerSingleton:LeaseTtlSeconds": "120"
        }
      }
      """);

    fixture.WriteKeyVaultSnapshot(
      """
      {
        "otlp-primary": "http://localhost:4317"
      }
      """);

    var builder = Host.CreateApplicationBuilder();
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
      [$"{SignalStackBootstrapOptions.SectionName}:AppConfiguration:SnapshotPath"] = fixture.AppConfigurationPath,
      [$"{SignalStackBootstrapOptions.SectionName}:KeyVault:SnapshotPath"] = fixture.KeyVaultPath,
      [$"{SignalStackBootstrapOptions.SectionName}:LastKnownGood:CachePath"] = fixture.CachePath,
      [$"{SignalStackBootstrapOptions.SectionName}:LastKnownGood:MaxAgeSeconds"] = "86400",
      [$"{SignalStackBootstrapOptions.SectionName}:LastKnownGood:StalenessAlertThresholdSeconds"] = "21600"
    });

    builder.AddSignalStackBootstrapConfiguration("SignalStack.Worker");

    using var host = builder.Build();

    var state = host.Services.GetRequiredService<ConfigurationBootstrapState>();
    var config = host.Services.GetRequiredService<IConfiguration>();

    Assert.Equal(ConfigurationSourceKind.AzureAppConfiguration, state.SourceKind);
    Assert.True(state.AppConfigurationReachable);
    Assert.Equal("http://localhost:4317", config["Telemetry:Otlp:PrimaryEndpoint"]);
    Assert.Equal("120", config["WorkerSingleton:LeaseTtlSeconds"]);
  }

  private sealed class BootstrapFixture : IDisposable
  {
    public BootstrapFixture()
    {
      RootPath = Path.Combine(Path.GetTempPath(), "signalstack-worker-bootstrap-tests", Guid.NewGuid().ToString("N"));
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

    public void Dispose()
    {
      if (Directory.Exists(RootPath))
      {
        Directory.Delete(RootPath, recursive: true);
      }
    }
  }
}
