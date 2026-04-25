using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Configuration;
using SignalStack.Worker.Singleton;
using Xunit;

namespace SignalStack.Worker.Tests;

public sealed class WorkerSingletonCoordinatorTests
{
  [Fact]
  public async Task AcquireStartupLease_returns_failure_when_a_different_holder_owns_the_lease()
  {
    var coordinator = new WorkerSingletonCoordinator(
      new StaticWorkerInstanceIdentityProvider("worker-a"),
      new StubWorkerSingletonLeaseBackend(WorkerLeaseAcquireResult.Conflict("worker-b")),
      NullLogger<WorkerSingletonCoordinator>.Instance,
      Options.Create(new WorkerSingletonOptions()));

    var decision = await coordinator.AcquireStartupLeaseAsync(CancellationToken.None);

    Assert.False(decision.ShouldRunHeartbeat);
    Assert.True(decision.ShouldExitWithFailure);
    Assert.Equal("worker-b", decision.DetectedHolderInstanceId);
  }

  [Fact]
  public async Task AcquireStartupLease_allows_startup_when_redis_is_unavailable()
  {
    var coordinator = new WorkerSingletonCoordinator(
      new StaticWorkerInstanceIdentityProvider("worker-a"),
      new StubWorkerSingletonLeaseBackend(WorkerLeaseAcquireResult.Unavailable("redis_timeout")),
      NullLogger<WorkerSingletonCoordinator>.Instance,
      Options.Create(new WorkerSingletonOptions()));

    var decision = await coordinator.AcquireStartupLeaseAsync(CancellationToken.None);

    Assert.True(decision.ShouldRunHeartbeat);
    Assert.False(decision.ShouldExitWithFailure);
  }

  private sealed class StaticWorkerInstanceIdentityProvider : IWorkerInstanceIdentityProvider
  {
    private readonly string _instanceId;

    public StaticWorkerInstanceIdentityProvider(string instanceId)
    {
      _instanceId = instanceId;
    }

    public string GetInstanceId() => _instanceId;
  }

  private sealed class StubWorkerSingletonLeaseBackend : IWorkerSingletonLeaseBackend
  {
    private readonly WorkerLeaseAcquireResult _acquireResult;

    public StubWorkerSingletonLeaseBackend(WorkerLeaseAcquireResult acquireResult)
    {
      _acquireResult = acquireResult;
    }

    public Task<WorkerLeaseAcquireResult> TryAcquireAsync(string leaseKey, string instanceId, TimeSpan leaseTtl, CancellationToken cancellationToken)
    {
      return Task.FromResult(_acquireResult);
    }

    public Task<WorkerLeaseRefreshResult> TryRefreshAsync(string leaseKey, string instanceId, TimeSpan leaseTtl, CancellationToken cancellationToken)
    {
      return Task.FromResult(WorkerLeaseRefreshResult.Refreshed());
    }
  }
}
