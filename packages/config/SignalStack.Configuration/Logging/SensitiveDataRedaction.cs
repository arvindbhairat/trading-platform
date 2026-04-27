using OpenTelemetry;
using OpenTelemetry.Logs;
using Serilog.Core;
using Serilog.Events;

namespace SignalStack.Configuration.Logging;

public static class SensitiveDataRedaction
{
  public const string RedactedValue = "[REDACTED]";

  private static readonly string[] SensitiveNameFragments =
  [
    "token",
    "access_token",
    "authorization",
    "secret",
    "password",
    "api_key",
    "apikey"
  ];

  public static bool ShouldRedact(string? propertyName)
  {
    if (string.IsNullOrWhiteSpace(propertyName))
    {
      return false;
    }

    return SensitiveNameFragments.Any(fragment =>
      propertyName.Contains(fragment, StringComparison.OrdinalIgnoreCase));
  }

  public static void Redact(LogEvent logEvent)
  {
    ArgumentNullException.ThrowIfNull(logEvent);

    foreach (var property in logEvent.Properties.ToArray())
    {
      if (!ShouldRedact(property.Key))
      {
        continue;
      }

      logEvent.AddOrUpdateProperty(new LogEventProperty(property.Key, new ScalarValue(RedactedValue)));
    }
  }

  public static IReadOnlyList<KeyValuePair<string, object?>> RedactAttributes(
    IEnumerable<KeyValuePair<string, object?>>? attributes)
  {
    if (attributes is null)
    {
      return [];
    }

    return attributes
      .Select(attribute => ShouldRedact(attribute.Key)
        ? new KeyValuePair<string, object?>(attribute.Key, RedactedValue)
        : attribute)
      .ToArray();
  }
}

public sealed class SensitiveDataRedactionEnricher : ILogEventEnricher
{
  public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
  {
    SensitiveDataRedaction.Redact(logEvent);
  }
}

public sealed class SensitiveDataRedactionLogProcessor : BaseProcessor<LogRecord>
{
  public override void OnEnd(LogRecord data)
  {
    ArgumentNullException.ThrowIfNull(data);

    if (data.Attributes is not null)
    {
      data.Attributes = SensitiveDataRedaction.RedactAttributes(data.Attributes);
    }
  }
}
