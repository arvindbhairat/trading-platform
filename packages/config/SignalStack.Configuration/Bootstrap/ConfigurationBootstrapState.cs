namespace SignalStack.Configuration.Bootstrap;

public sealed class ConfigurationBootstrapState
{
  private readonly object _sync = new();

  public ConfigurationBootstrapState(string serviceName, LastKnownGoodBootstrapOptions options)
  {
    ServiceName = serviceName;
    MaxAgeSeconds = options.MaxAgeSeconds;
    StalenessAlertThresholdSeconds = options.StalenessAlertThresholdSeconds;
  }

  public string ServiceName { get; }
  public int MaxAgeSeconds { get; }
  public int StalenessAlertThresholdSeconds { get; }
  public DateTimeOffset LastSuccessfulReadUtc { get; private set; }
  public ConfigurationSourceKind SourceKind { get; private set; }
  public bool AppConfigurationReachable { get; private set; }
  public bool StalenessAlertRaised { get; private set; }
  public bool AppConfigurationFailureLogged { get; private set; }

  public void Initialize(ConfigurationBootstrapResult result)
  {
    lock (_sync)
    {
      LastSuccessfulReadUtc = result.LastSuccessfulReadUtc;
      SourceKind = result.SourceKind;
      AppConfigurationReachable = result.AppConfigurationReachable;
      StalenessAlertRaised = false;
      AppConfigurationFailureLogged = !result.AppConfigurationReachable;
    }
  }

  public void MarkSuccessfulRemoteRead(DateTimeOffset timestampUtc)
  {
    lock (_sync)
    {
      LastSuccessfulReadUtc = timestampUtc;
      SourceKind = ConfigurationSourceKind.AzureAppConfiguration;
      AppConfigurationReachable = true;
      StalenessAlertRaised = false;
      AppConfigurationFailureLogged = false;
    }
  }

  public bool MarkRemoteFailure()
  {
    lock (_sync)
    {
      var shouldLog = !AppConfigurationFailureLogged;
      AppConfigurationReachable = false;
      AppConfigurationFailureLogged = true;
      return shouldLog;
    }
  }

  public double GetSecondsSinceLastSuccessfulRead(DateTimeOffset utcNow)
  {
    lock (_sync)
    {
      return Math.Max(0, (utcNow - LastSuccessfulReadUtc).TotalSeconds);
    }
  }

  public bool ShouldRaiseStalenessAlert(DateTimeOffset utcNow)
  {
    lock (_sync)
    {
      if (AppConfigurationReachable || StalenessAlertRaised)
      {
        return false;
      }

      if (Math.Max(0, (utcNow - LastSuccessfulReadUtc).TotalSeconds) <= StalenessAlertThresholdSeconds)
      {
        return false;
      }

      StalenessAlertRaised = true;
      return true;
    }
  }
}
