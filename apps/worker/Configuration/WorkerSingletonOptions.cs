using System.ComponentModel.DataAnnotations;

namespace SignalStack.Worker.Configuration;

public sealed class WorkerSingletonOptions
{
  public const string SectionName = "WorkerSingleton";

  [Required]
  public string LeaseKey { get; init; } = "rme:worker:singleton";

  [Range(1, 300)]
  public int LeaseTtlSeconds { get; init; } = 60;

  [Range(1, 300)]
  public int RefreshIntervalSeconds { get; init; } = 20;

  [Range(1, 300)]
  public int HeartbeatIntervalSeconds { get; init; } = 10;
}
