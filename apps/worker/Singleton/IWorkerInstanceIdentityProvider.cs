namespace SignalStack.Worker.Singleton;

internal interface IWorkerInstanceIdentityProvider
{
  string GetInstanceId();
}
