using System.Text.Json;

namespace SignalStack.Configuration.Bootstrap;

public sealed class LastKnownGoodCacheStore
{
  public LastKnownGoodCacheStore(string cachePath)
  {
    CachePath = cachePath;
  }

  public string CachePath { get; }

  public ConfigurationBootstrapResult ReadFreshCache(int maxAgeSeconds, DateTimeOffset utcNow)
  {
    if (string.IsNullOrWhiteSpace(CachePath) || !File.Exists(CachePath))
    {
      throw new InvalidOperationException("No last-known-good cache file is available.");
    }

    var document = JsonSerializer.Deserialize<LastKnownGoodCacheDocument>(File.ReadAllText(CachePath))
      ?? throw new InvalidOperationException("The last-known-good cache file could not be parsed.");

    var age = utcNow - document.LastSuccessfulReadUtc;
    if (age > TimeSpan.FromSeconds(maxAgeSeconds))
    {
      throw new InvalidOperationException(
        $"The last-known-good cache is stale ({age.TotalSeconds:F0}s old, max {maxAgeSeconds}s).");
    }

    return new ConfigurationBootstrapResult
    {
      Values = new Dictionary<string, string?>(document.Values, StringComparer.OrdinalIgnoreCase),
      LastSuccessfulReadUtc = document.LastSuccessfulReadUtc,
      SourceKind = ConfigurationSourceKind.LastKnownGoodCache,
      AppConfigurationReachable = false
    };
  }

  public void Write(IReadOnlyDictionary<string, string?> values, DateTimeOffset lastSuccessfulReadUtc)
  {
    if (string.IsNullOrWhiteSpace(CachePath))
    {
      return;
    }

    var directory = Path.GetDirectoryName(CachePath);
    if (!string.IsNullOrWhiteSpace(directory))
    {
      Directory.CreateDirectory(directory);
    }

    var tempPath = $"{CachePath}.tmp";
    var document = new LastKnownGoodCacheDocument
    {
      LastSuccessfulReadUtc = lastSuccessfulReadUtc,
      Values = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase)
    };

    File.WriteAllText(tempPath, JsonSerializer.Serialize(document, new JsonSerializerOptions
    {
      WriteIndented = true
    }));

    File.Move(tempPath, CachePath, overwrite: true);
  }

  private sealed class LastKnownGoodCacheDocument
  {
    public DateTimeOffset LastSuccessfulReadUtc { get; set; }
    public Dictionary<string, string?> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
  }
}
