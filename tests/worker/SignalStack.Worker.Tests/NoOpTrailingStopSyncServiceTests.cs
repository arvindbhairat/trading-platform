using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests for <see cref="NoOpTrailingStopSyncService"/> — the Phase 6 placeholder
/// for synchronous trailing-stop recalculation (ADR-0003 carve-out).
/// </summary>
public sealed class NoOpTrailingStopSyncServiceTests
{
    private static NoOpTrailingStopSyncService CreateService() =>
        new(NullLogger<NoOpTrailingStopSyncService>.Instance);

    [Fact]
    public async Task RecalculateAllAsync_completes_without_throwing()
    {
        var service = CreateService();
        var exception = await Record.ExceptionAsync(() => service.RecalculateAllAsync(default));
        Assert.Null(exception);
    }

    [Fact]
    public async Task RecalculateForPositionAsync_completes_without_throwing()
    {
        var service = CreateService();
        var exception = await Record.ExceptionAsync(
            () => service.RecalculateForPositionAsync(Guid.NewGuid(), default));
        Assert.Null(exception);
    }
}
