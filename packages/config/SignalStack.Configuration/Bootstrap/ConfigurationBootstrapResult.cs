namespace SignalStack.Configuration.Bootstrap;

public sealed class ConfigurationBootstrapResult
{
  public required IReadOnlyDictionary<string, string?> Values { get; init; }
  public required DateTimeOffset LastSuccessfulReadUtc { get; init; }
  public required ConfigurationSourceKind SourceKind { get; init; }
  public required bool AppConfigurationReachable { get; init; }
}

public enum ConfigurationSourceKind
{
  AzureAppConfiguration = 0,
  LastKnownGoodCache = 1
}
