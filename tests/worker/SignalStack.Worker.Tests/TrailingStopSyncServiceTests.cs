using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests the synchronous EOD trailing-stop recalculation service (P6-T15).
/// Verifies the ADR-0003 carve-out (synchronous, non-decreasing invariant)
/// and ATR-based trailing stop application.
/// </summary>
public sealed class TrailingStopSyncServiceTests
{
    // ─── RecalculateAllAsync processes all non-terminal positions ──────────

    [Fact]
    public async Task RecalculateAllAsync_processes_all_non_terminal_positions()
    {
        // Arrange
        var positions = new List<PositionDocument>
        {
            CreatePosition(Guid.NewGuid(), PositionState.Open, 1000m, 1100m, 950m, 1),
            CreatePosition(Guid.NewGuid(), PositionState.PendingEntry, 500m, 500m, 0m, 1),
        };

        var repo = new Mock<IPositionRepository>();
        repo.Setup(r => r.GetNonTerminalAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(positions);

        var atrStop = new Mock<IStopLoss>();
        atrStop.Setup(s => s.Name).Returns("AtrTrailingStop");
        atrStop.Setup(s => s.Calculate(It.IsAny<StopLossInput>()))
            .Returns(new StopLossResult(StopPrice: 1050m, StopTypeUsed: "AtrTrailingStop"));

        // Both positions should see OCC success so the service can update them.
        repo.Setup(r => r.UpdateWithOccAsync(
                It.IsAny<PositionDocument>(),
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((PositionDocument _, long _, long? _, CancellationToken _) =>
                new PositionOccResult(true, 2));

        var service = CreateService(repo.Object, atrStop.Object);

        // Act
        await service.RecalculateAllAsync();

        // Assert: both positions were updated via the repository
        repo.Verify(r => r.GetNonTerminalAsync(It.IsAny<CancellationToken>()), Times.Once);
        repo.Verify(r => r.UpdateWithOccAsync(
            It.IsAny<PositionDocument>(),
            It.IsAny<long>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    // ─── Non-decreasing invariant: skips when new stop <= existing stop ────

    [Fact]
    public async Task RecalculateForPositionAsync_skips_when_stop_not_increased()
    {
        // Arrange: position has existing trailing stop of 1100
        // Calculator returns 1050 (lower) → should skip
        var positionId = Guid.NewGuid();
        var position = CreatePosition(positionId, PositionState.Open, 1000m, 1200m, 1100m, 1);

        var repo = new Mock<IPositionRepository>();
        repo.Setup(r => r.GetByIdAsync(positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);

        var atrStop = new Mock<IStopLoss>();
        atrStop.Setup(s => s.Name).Returns("AtrTrailingStop");
        atrStop.Setup(s => s.Calculate(It.IsAny<StopLossInput>()))
            .Returns(new StopLossResult(StopPrice: 1050m, StopTypeUsed: "AtrTrailingStop"));

        var service = CreateService(repo.Object, atrStop.Object);

        // Act
        await service.RecalculateForPositionAsync(positionId);

        // Assert: UpdateWithOccAsync was NOT called because the stop didn't increase
        repo.Verify(r => r.UpdateWithOccAsync(
            It.IsAny<PositionDocument>(),
            It.IsAny<long>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Non-decreasing: updates when new stop is higher ──────────────────

    [Fact]
    public async Task RecalculateForPositionAsync_updates_when_stop_increased()
    {
        // Arrange: position has existing trailing stop of 1000
        // Calculator returns 1100 (higher) → should update
        var positionId = Guid.NewGuid();
        var position = CreatePosition(positionId, PositionState.Open, 1000m, 1300m, 1000m, 1);

        var repo = new Mock<IPositionRepository>();
        repo.Setup(r => r.GetByIdAsync(positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        repo.Setup(r => r.UpdateWithOccAsync(
                It.IsAny<PositionDocument>(),
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionOccResult(true, 2));

        var atrStop = new Mock<IStopLoss>();
        atrStop.Setup(s => s.Name).Returns("AtrTrailingStop");
        atrStop.Setup(s => s.Calculate(It.IsAny<StopLossInput>()))
            .Returns(new StopLossResult(StopPrice: 1100m, StopTypeUsed: "AtrTrailingStop"));

        var service = CreateService(repo.Object, atrStop.Object);

        // Act
        await service.RecalculateForPositionAsync(positionId);

        // Assert
        repo.Verify(r => r.UpdateWithOccAsync(
            It.IsAny<PositionDocument>(),
            1, // expected version
            null, // no fencing token
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Updates when existing trailing stop is zero (first recalculation) ──

    [Fact]
    public async Task RecalculateForPositionAsync_updates_when_existing_stop_is_zero()
    {
        // Arrange: position has no existing trailing stop (0)
        // Calculator returns 900 → should update (first EOD recalculation)
        var positionId = Guid.NewGuid();
        var position = CreatePosition(positionId, PositionState.Open, 1000m, 1000m, 0m, 1);

        var repo = new Mock<IPositionRepository>();
        repo.Setup(r => r.GetByIdAsync(positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        repo.Setup(r => r.UpdateWithOccAsync(
                It.IsAny<PositionDocument>(),
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionOccResult(true, 2));

        var atrStop = new Mock<IStopLoss>();
        atrStop.Setup(s => s.Name).Returns("AtrTrailingStop");
        atrStop.Setup(s => s.Calculate(It.IsAny<StopLossInput>()))
            .Returns(new StopLossResult(StopPrice: 900m, StopTypeUsed: "AtrTrailingStop"));

        var service = CreateService(repo.Object, atrStop.Object);

        // Act
        await service.RecalculateForPositionAsync(positionId);

        // Assert
        repo.Verify(r => r.UpdateWithOccAsync(
            It.IsAny<PositionDocument>(),
            It.IsAny<long>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Handles OCC conflict gracefully ──────────────────────────────────

    [Fact]
    public async Task RecalculateForPositionAsync_handles_occ_conflict_gracefully()
    {
        // Arrange: OCC conflict (stale version)
        var positionId = Guid.NewGuid();
        var position = CreatePosition(positionId, PositionState.Open, 1000m, 1200m, 950m, 1);

        var repo = new Mock<IPositionRepository>();
        repo.Setup(r => r.GetByIdAsync(positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        repo.Setup(r => r.UpdateWithOccAsync(
                It.IsAny<PositionDocument>(),
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionOccResult(false, 0));

        var atrStop = new Mock<IStopLoss>();
        atrStop.Setup(s => s.Name).Returns("AtrTrailingStop");
        atrStop.Setup(s => s.Calculate(It.IsAny<StopLossInput>()))
            .Returns(new StopLossResult(StopPrice: 1050m, StopTypeUsed: "AtrTrailingStop"));

        var service = CreateService(repo.Object, atrStop.Object);

        // Act - should not throw
        await service.RecalculateForPositionAsync(positionId);

        // Assert
        repo.Verify(r => r.UpdateWithOccAsync(
            It.IsAny<PositionDocument>(),
            It.IsAny<long>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // ─── Position not found → no-op ────────────────────────────────────────

    [Fact]
    public async Task RecalculateForPositionAsync_noop_when_position_not_found()
    {
        // Arrange
        var positionId = Guid.NewGuid();

        var repo = new Mock<IPositionRepository>();
        repo.Setup(r => r.GetByIdAsync(positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((PositionDocument?)null);

        var atrStop = new Mock<IStopLoss>();
        atrStop.Setup(s => s.Name).Returns("AtrTrailingStop");

        var service = CreateService(repo.Object, atrStop.Object);

        // Act
        await service.RecalculateForPositionAsync(positionId);

        // Assert
        repo.Verify(r => r.UpdateWithOccAsync(
            It.IsAny<PositionDocument>(),
            It.IsAny<long>(),
            It.IsAny<long?>(),
            It.IsAny<CancellationToken>()), Times.Never);
    }

    // ─── Uses ATR trailing stop calculator ─────────────────────────────────

    [Fact]
    public async Task RecalculateForPositionAsync_uses_atr_trailing_stop_calculator()
    {
        // Arrange
        var positionId = Guid.NewGuid();
        var position = CreatePosition(positionId, PositionState.Open, 1000m, 1200m, 900m, 1);

        var repo = new Mock<IPositionRepository>();
        repo.Setup(r => r.GetByIdAsync(positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        repo.Setup(r => r.UpdateWithOccAsync(
                It.IsAny<PositionDocument>(),
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionOccResult(true, 2));

        StopLossInput? capturedInput = null;
        var atrStop = new Mock<IStopLoss>();
        atrStop.Setup(s => s.Name).Returns("AtrTrailingStop");
        atrStop.Setup(s => s.Calculate(It.IsAny<StopLossInput>()))
            .Callback<StopLossInput>(i => capturedInput = i)
            .Returns(new StopLossResult(StopPrice: 1100m, StopTypeUsed: "AtrTrailingStop"));

        var service = CreateService(repo.Object, atrStop.Object);

        // Act
        await service.RecalculateForPositionAsync(positionId);

        // Assert: calculator received the correct input
        Assert.NotNull(capturedInput);
        Assert.Equal(1000m, capturedInput!.EntryPrice);
        Assert.Contains("HighestPriceSinceEntry", capturedInput!.AdditionalInputs!);
        Assert.Equal(1200m, capturedInput!.AdditionalInputs!["HighestPriceSinceEntry"]);
    }

    // ─── Uses current price as highest when current > entry ────────────────

    [Fact]
    public async Task RecalculateForPositionAsync_uses_current_price_as_highest()
    {
        // Arrange: current price (1500) > entry (1000)
        var positionId = Guid.NewGuid();
        var position = CreatePosition(positionId, PositionState.Open, 1000m, 1500m, 900m, 1);

        var repo = new Mock<IPositionRepository>();
        repo.Setup(r => r.GetByIdAsync(positionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(position);
        repo.Setup(r => r.UpdateWithOccAsync(
                It.IsAny<PositionDocument>(),
                It.IsAny<long>(),
                It.IsAny<long?>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PositionOccResult(true, 2));

        StopLossInput? capturedInput = null;
        var atrStop = new Mock<IStopLoss>();
        atrStop.Setup(s => s.Name).Returns("AtrTrailingStop");
        atrStop.Setup(s => s.Calculate(It.IsAny<StopLossInput>()))
            .Callback<StopLossInput>(i => capturedInput = i)
            .Returns(new StopLossResult(StopPrice: 1400m, StopTypeUsed: "AtrTrailingStop"));

        var service = CreateService(repo.Object, atrStop.Object);

        // Act
        await service.RecalculateForPositionAsync(positionId);

        // Assert
        Assert.Equal(1500m, capturedInput!.AdditionalInputs!["HighestPriceSinceEntry"]);
    }

    // ─── Constructor throws when AtrTrailingStop is not registered ─────────

    [Fact]
    public void Constructor_throws_when_atr_trailing_stop_not_registered()
    {
        // Arrange: register only a different stop
        var stopLosses = new List<IStopLoss>
        {
            new FixedStopLoss(
                Options.Create(new RmeModuleOptions()),
                NullLogger<FixedStopLoss>.Instance)
        };

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new TrailingStopSyncService(
                Mock.Of<IPositionRepository>(),
                stopLosses,
                NullLogger<TrailingStopSyncService>.Instance));

        Assert.Contains("AtrTrailingStop", ex.Message);
    }

    // ─── Helpers ────────────────────────────────────────────────────────────

    private static TrailingStopSyncService CreateService(
        IPositionRepository repository,
        IStopLoss atrTrailingStop)
    {
        var stopLosses = new List<IStopLoss> { atrTrailingStop };
        return new TrailingStopSyncService(
            repository,
            stopLosses,
            NullLogger<TrailingStopSyncService>.Instance);
    }

    private static PositionDocument CreatePosition(
        Guid positionId,
        PositionState state,
        decimal entryPrice,
        decimal currentPrice,
        decimal trailingStop,
        long version)
    {
        return new PositionDocument
        {
            PositionId = positionId,
            UserId = "user-test-1",
            Symbol = "TEST",
            State = state,
            EntryPrice = entryPrice,
            CurrentPrice = currentPrice,
            Quantity = 10m,
            StopLoss = entryPrice * 0.95m,
            TrailingStop = trailingStop,
            Version = version,
            CreatedAt = DateTime.UtcNow.AddDays(-5),
            UpdatedAt = DateTime.UtcNow,
        };
    }
}
