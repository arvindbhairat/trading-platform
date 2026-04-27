namespace SignalStack.Configuration.Bootstrap;

public sealed class SignalStackBootstrapOptions
{
  public const string SectionName = "SignalStack:Bootstrap";

  public AzureAppConfigurationBootstrapOptions AppConfiguration { get; set; } = new();
  public KeyVaultBootstrapOptions KeyVault { get; set; } = new();
  public LastKnownGoodBootstrapOptions LastKnownGood { get; set; } = new();
}

public sealed class AzureAppConfigurationBootstrapOptions
{
  public string Endpoint { get; set; } = string.Empty;
  public string SnapshotPath { get; set; } = string.Empty;
  public int RefreshIntervalSeconds { get; set; } = 300;
}

public sealed class KeyVaultBootstrapOptions
{
  public string VaultUri { get; set; } = string.Empty;
  public string SnapshotPath { get; set; } = string.Empty;
}

public sealed class LastKnownGoodBootstrapOptions
{
  public string CachePath { get; set; } = string.Empty;
  public int MaxAgeSeconds { get; set; } = 86400;
  public int StalenessAlertThresholdSeconds { get; set; } = 21600;
}
