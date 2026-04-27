using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Configuration;
using Serilog.Events;

namespace SignalStack.Configuration.Logging;

public static class SignalStackSerilogExtensions
{
  public static LoggerConfiguration ConfigureSignalStackLogger(
    this LoggerConfiguration loggerConfiguration,
    IConfiguration configuration,
    string serviceName,
    string environmentName,
    Action<LoggerSinkConfiguration> configureSinks)
  {
    ArgumentNullException.ThrowIfNull(loggerConfiguration);
    ArgumentNullException.ThrowIfNull(configuration);
    ArgumentNullException.ThrowIfNull(configureSinks);

    ApplyConfiguredLevels(loggerConfiguration, configuration);

    loggerConfiguration
      .Enrich.FromLogContext()
      .Enrich.WithProperty("service", serviceName)
      .Enrich.WithProperty("environment", environmentName)
      .Enrich.With<SensitiveDataRedactionEnricher>();

    configureSinks(loggerConfiguration.WriteTo);
    return loggerConfiguration;
  }

  private static void ApplyConfiguredLevels(LoggerConfiguration loggerConfiguration, IConfiguration configuration)
  {
    var logLevels = configuration.GetSection("Logging:LogLevel");
    var defaultLevel = ParseLogLevel(logLevels["Default"], LogEventLevel.Information);

    loggerConfiguration.MinimumLevel.Is(defaultLevel);

    foreach (var child in logLevels.GetChildren())
    {
      if (string.Equals(child.Key, "Default", StringComparison.OrdinalIgnoreCase))
      {
        continue;
      }

      loggerConfiguration.MinimumLevel.Override(child.Key, ParseLogLevel(child.Value, defaultLevel));
    }
  }

  private static LogEventLevel ParseLogLevel(string? configuredValue, LogEventLevel fallbackLevel)
  {
    return Enum.TryParse<LogEventLevel>(configuredValue, ignoreCase: true, out var parsed)
      ? parsed
      : fallbackLevel;
  }
}
