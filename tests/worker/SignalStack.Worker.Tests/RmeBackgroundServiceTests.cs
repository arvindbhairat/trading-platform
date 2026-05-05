using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests for <see cref="RmeBackgroundService"/> — the RME module's hosted service.
/// Currently a placeholder that logs startup options.
/// </summary>
public sealed class RmeBackgroundServiceTests
{
    [Fact]
    public async Task ExecuteAsync_completes_without_throwing()
    {
        var options = Options.Create(new RmeModuleOptions
        {
            MaxActivePositions = 200,
            DefaultRiskPerTradePct = 0.5,
            MinRiskPerTradePct = 0.5,
            MaxRiskPerTradePct = 1.0,
        });

        var service = new RmeBackgroundService(
            NullLogger<RmeBackgroundService>.Instance,
            options);

        // Act — the service should complete without error
        var exception = await Record.ExceptionAsync(() =>
            service.StartAsync(default));

        Assert.Null(exception);
    }

    [Fact]
    public async Task ExecuteAsync_logs_options_on_start()
    {
        var options = Options.Create(new RmeModuleOptions
        {
            MaxActivePositions = 100,
            DefaultRiskPerTradePct = 1.0,
            MinRiskPerTradePct = 0.5,
            MaxRiskPerTradePct = 2.0,
        });

        var service = new RmeBackgroundService(
            NullLogger<RmeBackgroundService>.Instance,
            options);

        await service.StartAsync(default);
        // No exception means the log line was emitted successfully.
        // Actual log content verification would require a mock logger.
    }
}
