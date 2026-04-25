namespace SignalStack.Worker.Singleton;

internal sealed record WorkerStartupDecision(bool ShouldRunHeartbeat, bool ShouldExitWithFailure, string? DetectedHolderInstanceId)
{
  public static WorkerStartupDecision Run() => new(true, false, null);

  public static WorkerStartupDecision ExitWithFailure(string? detectedHolderInstanceId) =>
    new(false, true, detectedHolderInstanceId);
}
