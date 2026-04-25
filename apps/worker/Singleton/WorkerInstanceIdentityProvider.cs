namespace SignalStack.Worker.Singleton;

internal sealed class WorkerInstanceIdentityProvider : IWorkerInstanceIdentityProvider
{
  public string GetInstanceId()
  {
    return Environment.GetEnvironmentVariable("WEBSITE_INSTANCE_ID")
      ?? Environment.GetEnvironmentVariable("HOSTNAME")
      ?? Environment.MachineName;
  }
}
