using System.Text.Json;

namespace SignalStack.Configuration.Bootstrap;

public sealed class BootstrapConfigurationLoader
{
  private const string KeyVaultReferencePrefix = "@kv:";

  public ConfigurationBootstrapResult LoadInitialConfiguration(
    SignalStackBootstrapOptions options,
    string contentRootPath,
    DateTimeOffset utcNow)
  {
    var cacheStore = new LastKnownGoodCacheStore(options.LastKnownGood.CachePath);

    try
    {
      var remoteValues = LoadRemoteValues(options, contentRootPath);
      cacheStore.Write(remoteValues, utcNow);

      return new ConfigurationBootstrapResult
      {
        Values = remoteValues,
        LastSuccessfulReadUtc = utcNow,
        SourceKind = ConfigurationSourceKind.AzureAppConfiguration,
        AppConfigurationReachable = true
      };
    }
    catch
    {
      return cacheStore.ReadFreshCache(options.LastKnownGood.MaxAgeSeconds, utcNow);
    }
  }

  public IReadOnlyDictionary<string, string?> LoadRemoteValues(SignalStackBootstrapOptions options, string contentRootPath)
  {
    var appConfigValues = ReadConfigurationValues(ResolvePath(contentRootPath, options.AppConfiguration.SnapshotPath));
    var keyVaultSecrets = ReadSecretValues(ResolvePath(contentRootPath, options.KeyVault.SnapshotPath));

    var resolved = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
    foreach (var pair in appConfigValues)
    {
      resolved[pair.Key] = ResolveValue(pair.Value, keyVaultSecrets);
    }

    return resolved;
  }

  private static Dictionary<string, string?> ReadConfigurationValues(string path)
  {
    if (!File.Exists(path))
    {
      throw new FileNotFoundException($"Azure App Configuration snapshot file was not found: {path}", path);
    }

    var document = JsonSerializer.Deserialize<AppConfigurationSnapshotDocument>(File.ReadAllText(path))
      ?? throw new InvalidOperationException($"Azure App Configuration snapshot file was invalid: {path}");

    return document.Values;
  }

  private static Dictionary<string, string?> ReadSecretValues(string path)
  {
    if (!File.Exists(path))
    {
      throw new FileNotFoundException($"Key Vault snapshot file was not found: {path}", path);
    }

    return JsonSerializer.Deserialize<Dictionary<string, string?>>(File.ReadAllText(path))
      ?? throw new InvalidOperationException($"Key Vault snapshot file was invalid: {path}");
  }

  private static string? ResolveValue(string? rawValue, IReadOnlyDictionary<string, string?> secrets)
  {
    if (string.IsNullOrWhiteSpace(rawValue) || !rawValue.StartsWith(KeyVaultReferencePrefix, StringComparison.OrdinalIgnoreCase))
    {
      return rawValue;
    }

    var secretName = rawValue[KeyVaultReferencePrefix.Length..];
    if (!secrets.TryGetValue(secretName, out var secretValue))
    {
      throw new InvalidOperationException($"Key Vault secret '{secretName}' was not found in the configured snapshot.");
    }

    return secretValue;
  }

  private static string ResolvePath(string contentRootPath, string configuredPath)
  {
    if (string.IsNullOrWhiteSpace(configuredPath))
    {
      throw new InvalidOperationException("A required bootstrap snapshot path was not configured.");
    }

    return Path.IsPathRooted(configuredPath)
      ? configuredPath
      : Path.GetFullPath(Path.Combine(contentRootPath, configuredPath));
  }

  private sealed class AppConfigurationSnapshotDocument
  {
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
  }
}
