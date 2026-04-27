using Microsoft.Extensions.Configuration;

namespace SignalStack.Configuration.Bootstrap;

public sealed class MutableConfigurationSource : IConfigurationSource
{
  public MutableConfigurationSource(IReadOnlyDictionary<string, string?> initialValues)
  {
    Provider = new MutableConfigurationProvider(initialValues);
  }

  public MutableConfigurationProvider Provider { get; }

  public IConfigurationProvider Build(IConfigurationBuilder builder)
  {
    return Provider;
  }
}

public sealed class MutableConfigurationProvider : ConfigurationProvider
{
  public MutableConfigurationProvider(IReadOnlyDictionary<string, string?> initialValues)
  {
    Replace(initialValues);
  }

  public void Replace(IReadOnlyDictionary<string, string?> values)
  {
    Data = new Dictionary<string, string?>(values, StringComparer.OrdinalIgnoreCase);
    OnReload();
  }
}
