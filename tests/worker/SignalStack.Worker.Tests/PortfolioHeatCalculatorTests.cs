using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SignalStack.Worker.Rme;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Tests for <see cref="PortfolioHeatCalculator"/> — portfolio heat calculation,
/// maximum enforcement, and correlated-industry grouping (REQ-HEAT-001, REQ-HEAT-002,
/// REQ-HEAT-003, REQ-SIZING-014a).
/// </summary>
public sealed class PortfolioHeatCalculatorTests
{
    // ─── Helpers ──────────────────────────────────────────────────────────────────

    private static PortfolioHeatCalculator CreateCalculator(
        IPositionRepository? repo = null,
        IEquityBaseReader? equityReader = null,
        CorrelatedIndustryGroupOptions? groupOptions = null,
        RmeModuleOptions? moduleOptions = null)
    {
        repo ??= new InMemoryPositionRepository();
        equityReader ??= CreateEquityReader(1_00_000m);
        groupOptions ??= new CorrelatedIndustryGroupOptions();
        moduleOptions ??= CreateModuleOptions();

        return new PortfolioHeatCalculator(
            repo,
            equityReader,
            Options.Create(groupOptions),
            Options.Create(moduleOptions),
            NullLogger<PortfolioHeatCalculator>.Instance);
    }

    private static IEquityBaseReader CreateEquityReader(decimal equity)
    {
        var result = new EquityBaseResult
        {
            EquityBase = equity,
            Source = EquityBaseSource.AutoDerived,
            IsSnapshotConsistent = true,
            SnapshotRetryAttempts = 0,
            IsOverrideActive = false,
            IsOverrideStale = false
        };

        var mock = new Moq.Mock<IEquityBaseReader>();
        mock.Setup(r => r.GetEquityBaseAsync(Moq.It.IsAny<string>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static IEquityBaseReader CreateContendedEquityReader()
    {
        var result = new EquityBaseResult
        {
            EquityBase = 0m,
            Source = EquityBaseSource.SnapshotContentionDeferred,
            IsSnapshotConsistent = false,
            SnapshotRetryAttempts = 3,
            IsOverrideActive = false,
            IsOverrideStale = false,
            AdvisoryMessage = "Snapshot contention after 3 retries."
        };

        var mock = new Moq.Mock<IEquityBaseReader>();
        mock.Setup(r => r.GetEquityBaseAsync(Moq.It.IsAny<string>(), Moq.It.IsAny<CancellationToken>()))
            .ReturnsAsync(result);
        return mock.Object;
    }

    private static RmeModuleOptions CreateModuleOptions(double maxPortfolioHeatPct = 5.0)
    {
        return new RmeModuleOptions
        {
            MaxPortfolioHeatPct = maxPortfolioHeatPct,
            DefaultRiskPerTradePct = 0.5,
            MinRiskPerTradePct = 0.5,
            MaxRiskPerTradePct = 1.0,
            MaxActivePositions = 200,
        };
    }

    // ─── CalculateAsync: basic scenarios ──────────────────────────────────────────

    [Fact]
    public async Task CalculateAsync_returns_zero_heat_when_no_positions()
    {
        var calculator = CreateCalculator();

        var result = await calculator.CalculateAsync("user1");

        Assert.Equal(0m, result.CurrentHeatPct);
        Assert.Equal(0m, result.TotalOpenRisk);
        Assert.Equal(1_00_000m, result.AccountEquity);
        Assert.Equal(5m, result.MaxHeatPct);
        Assert.Equal(0, result.OpenPositionCount);
    }

    [Fact]
    public async Task CalculateAsync_computes_heat_from_single_open_position()
    {
        // Arrange: entry=500, stop=450, qty=100 → risk = 50 * 100 = 5,000
        // equity = 1,00,000 → heat = 5000/100000 * 100 = 5%
        var repo = new InMemoryPositionRepository();
        repo.AddPosition(new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "user1",
            Symbol = "SBIN",
            State = PositionState.Open,
            EntryPrice = 500m,
            StopLoss = 450m,
            Quantity = 100m,
            CurrentPrice = 520m,
        });

        var calculator = CreateCalculator(repo: repo);

        var result = await calculator.CalculateAsync("user1");

        Assert.Equal(5m, result.CurrentHeatPct);
        Assert.Equal(5_000m, result.TotalOpenRisk);
        Assert.Equal(1, result.OpenPositionCount);
    }

    [Fact]
    public async Task CalculateAsync_aggregates_risk_across_multiple_positions()
    {
        // Position 1: entry=500, stop=450, qty=100 → risk = 5,000
        // Position 2: entry=1000, stop=950, qty=50 → risk = 2,500
        // total risk = 7,500; equity = 1,00,000 → heat = 7.5%
        var repo = new InMemoryPositionRepository();
        repo.AddPosition(new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "user1",
            Symbol = "SBIN",
            State = PositionState.Open,
            EntryPrice = 500m,
            StopLoss = 450m,
            Quantity = 100m,
            CurrentPrice = 520m,
        });
        repo.AddPosition(new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "user1",
            Symbol = "RELIANCE",
            State = PositionState.Open,
            EntryPrice = 1000m,
            StopLoss = 950m,
            Quantity = 50m,
            CurrentPrice = 1020m,
        });

        var calculator = CreateCalculator(repo: repo);

        var result = await calculator.CalculateAsync("user1");

        Assert.Equal(7.5m, result.CurrentHeatPct);
        Assert.Equal(7_500m, result.TotalOpenRisk);
        Assert.Equal(2, result.OpenPositionCount);
    }

    [Fact]
    public async Task CalculateAsync_includes_suspended_positions_in_heat()
    {
        // Open position: risk = 5,000
        // Suspended position: entry=800, stop=750, qty=30 → risk = 1,500
        // total risk = 6,500; equity = 1,00,000 → heat = 6.5%
        var repo = new InMemoryPositionRepository();
        repo.AddPosition(new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "user1",
            Symbol = "SBIN",
            State = PositionState.Open,
            EntryPrice = 500m,
            StopLoss = 450m,
            Quantity = 100m,
            CurrentPrice = 520m,
        });
        repo.AddPosition(new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "user1",
            Symbol = "TATAMOTORS",
            State = PositionState.Suspended,
            EntryPrice = 800m,
            StopLoss = 750m,
            Quantity = 30m,
            CurrentPrice = 780m,
        });

        var calculator = CreateCalculator(repo: repo);

        var result = await calculator.CalculateAsync("user1");

        Assert.Equal(6.5m, result.CurrentHeatPct);
        Assert.Equal(6_500m, result.TotalOpenRisk);
        Assert.Equal(1, result.OpenPositionCount);
        Assert.Equal(1, result.SuspendedPositionCount);
    }

    [Fact]
    public async Task CalculateAsync_excludes_terminal_positions_from_heat()
    {
        // Open: risk = 5,000
        // Closed: risk = 2,500 (should be excluded)
        var repo = new InMemoryPositionRepository();
        repo.AddPosition(new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "user1",
            Symbol = "SBIN",
            State = PositionState.Open,
            EntryPrice = 500m,
            StopLoss = 450m,
            Quantity = 100m,
            CurrentPrice = 520m,
        });
        repo.AddPosition(new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "user1",
            Symbol = "RELIANCE",
            State = PositionState.Closed,
            EntryPrice = 1000m,
            StopLoss = 950m,
            Quantity = 50m,
            CurrentPrice = 980m,
        });

        var calculator = CreateCalculator(repo: repo);

        var result = await calculator.CalculateAsync("user1");

        Assert.Equal(5m, result.CurrentHeatPct);
        Assert.Equal(5_000m, result.TotalOpenRisk);
        Assert.Equal(1, result.OpenPositionCount);
    }

    [Fact]
    public async Task CalculateAsync_ignores_other_users_positions()
    {
        // Only return positions for the requested user
        var repo = new InMemoryPositionRepository();
        repo.AddPosition(new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "user1",
            Symbol = "SBIN",
            State = PositionState.Open,
            EntryPrice = 500m,
            StopLoss = 450m,
            Quantity = 100m,
            CurrentPrice = 520m,
        });
        repo.AddPosition(new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "user2",
            Symbol = "RELIANCE",
            State = PositionState.Open,
            EntryPrice = 1000m,
            StopLoss = 950m,
            Quantity = 100m,
            CurrentPrice = 1020m,
        });

        var calculator = CreateCalculator(repo: repo);

        // user1 should only see SBIN risk
        var result = await calculator.CalculateAsync("user1");

        Assert.Equal(5m, result.CurrentHeatPct);
        Assert.Equal(5_000m, result.TotalOpenRisk);
        Assert.Equal(1, result.OpenPositionCount);
    }

    [Fact]
    public async Task CalculateAsync_handles_zero_equity_gracefully()
    {
        // When equity base is zero, heat should be 0
        var zeroEquityReader = CreateEquityReader(0m);
        var calculator = CreateCalculator(equityReader: zeroEquityReader);

        var result = await calculator.CalculateAsync("user1");

        Assert.Equal(0m, result.CurrentHeatPct);
        Assert.Equal(0m, result.TotalOpenRisk);
        Assert.Equal(0m, result.AccountEquity);
    }

    [Fact]
    public async Task CalculateAsync_handles_snapshot_contention_gracefully()
    {
        var contendedReader = CreateContendedEquityReader();
        var calculator = CreateCalculator(equityReader: contendedReader);

        var result = await calculator.CalculateAsync("user1");

        Assert.Equal(0m, result.CurrentHeatPct);
        Assert.Equal(0m, result.TotalOpenRisk);
        Assert.Equal(0m, result.AccountEquity);
    }

    [Fact]
    public async Task CalculateAsync_respects_custom_max_heat_from_options()
    {
        var repo = new InMemoryPositionRepository();
        repo.AddPosition(new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "user1",
            Symbol = "SBIN",
            State = PositionState.Open,
            EntryPrice = 500m,
            StopLoss = 450m,
            Quantity = 100m,
            CurrentPrice = 520m,
        });

        var options = CreateModuleOptions(maxPortfolioHeatPct: 8.0);
        var calculator = CreateCalculator(repo: repo, moduleOptions: options);

        var result = await calculator.CalculateAsync("user1");

        Assert.Equal(5m, result.CurrentHeatPct);  // same heat, different max
        Assert.Equal(8m, result.MaxHeatPct);
    }

    [Fact]
    public async Task CalculateAsync_skips_positions_without_valid_stop()
    {
        // Position with stop >= entry price contributes 0 risk
        var repo = new InMemoryPositionRepository();
        repo.AddPosition(new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "user1",
            Symbol = "SBIN",
            State = PositionState.Open,
            EntryPrice = 500m,
            StopLoss = 500m,  // stop >= entry → no valid risk
            Quantity = 100m,
            CurrentPrice = 520m,
        });

        var calculator = CreateCalculator(repo: repo);

        var result = await calculator.CalculateAsync("user1");

        Assert.Equal(0m, result.CurrentHeatPct);
        Assert.Equal(0m, result.TotalOpenRisk);
    }

    [Fact]
    public async Task CalculateAsync_includes_pending_entry_positions()
    {
        var repo = new InMemoryPositionRepository();
        repo.AddPosition(new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "user1",
            Symbol = "SBIN",
            State = PositionState.PendingEntry,
            EntryPrice = 500m,
            StopLoss = 450m,
            Quantity = 100m,
            CurrentPrice = 0m,
        });

        var calculator = CreateCalculator(repo: repo);

        var result = await calculator.CalculateAsync("user1");

        Assert.Equal(5m, result.CurrentHeatPct);
        Assert.Equal(1, result.OpenPositionCount); // PendingEntry counted as open
    }

    // ─── AssertEntryAllowedAsync: maximum enforcement ─────────────────────────────

    [Fact]
    public async Task AssertEntryAllowedAsync_allows_entry_within_heat_limit()
    {
        // No positions → 0% current heat, max 5%
        // Proposed risk = 2,000 → projected heat = 2%
        var calculator = CreateCalculator();

        var result = await calculator.AssertEntryAllowedAsync("user1", 2_000m);

        Assert.True(result.Allowed);
        Assert.Equal(2m, result.ProjectedHeatPct);
        Assert.Equal(5m, result.MaxHeatPct);
        Assert.Null(result.AdvisoryMessage);
    }

    [Fact]
    public async Task AssertEntryAllowedAsync_blocks_entry_exceeding_heat_limit()
    {
        // Existing: heat = 4% (from existing positions)
        // Proposed risk: 2,000 → projected heat = 4 + 2 = 6% > 5%
        var repo = new InMemoryPositionRepository();
        repo.AddPosition(new PositionDocument
        {
            PositionId = Guid.NewGuid(),
            UserId = "user1",
            Symbol = "SBIN",
            State = PositionState.Open,
            EntryPrice = 500m,
            StopLoss = 450m,
            Quantity = 80m,  // risk = 50 * 80 = 4,000 → heat = 4%
            CurrentPrice = 520m,
        });

        var calculator = CreateCalculator(repo: repo);

        var result = await calculator.AssertEntryAllowedAsync("user1", 3_000m);

        Assert.False(result.Allowed);
        Assert.NotNull(result.AdvisoryMessage);
        // Projected heat: (4000+3000)/100000 * 100 = 7%
        Assert.Equal(7m, result.ProjectedHeatPct);
    }

    [Fact]
    public async Task AssertEntryAllowedAsync_allows_entry_at_exact_limit()
    {
        // No positions, equity = 1,00,000, max heat = 5%
        // Proposed risk = 5,000 → projected heat = 5% exactly = max
        var calculator = CreateCalculator();

        var result = await calculator.AssertEntryAllowedAsync("user1", 5_000m);

        Assert.True(result.Allowed);
        Assert.Equal(5m, result.ProjectedHeatPct);
    }

    [Fact]
    public async Task AssertEntryAllowedAsync_blocks_when_equity_is_zero()
    {
        var zeroEquityReader = CreateEquityReader(0m);
        var calculator = CreateCalculator(equityReader: zeroEquityReader);

        var result = await calculator.AssertEntryAllowedAsync("user1", 1_000m);

        Assert.False(result.Allowed);
        Assert.NotNull(result.AdvisoryMessage);
        Assert.Contains("equity base", result.AdvisoryMessage, StringComparison.OrdinalIgnoreCase);
    }

    // ─── Correlated industry groups ───────────────────────────────────────────────

    [Fact]
    public void CorrelatedIndustryGroupOptions_returns_default_groups_when_json_empty()
    {
        var options = new CorrelatedIndustryGroupOptions
        {
            GroupsJson = ""
        };

        var groups = options.GetGroups();

        Assert.Equal(3, groups.Count);
        Assert.Contains(groups, g => g.GroupName == "Banking & Finance");
        Assert.Contains(groups, g => g.GroupName == "Metals");
        Assert.Contains(groups, g => g.GroupName == "Oil & Gas");
    }

    [Fact]
    public void CorrelatedIndustryGroupOptions_parses_custom_groups_from_json()
    {
        var options = new CorrelatedIndustryGroupOptions
        {
            GroupsJson = """
            [
                {"group_name":"Tech","industry_names":["IT","Software"]}
            ]
            """
        };

        var groups = options.GetGroups();

        Assert.Single(groups);
        Assert.Equal("Tech", groups[0].GroupName);
        Assert.Equal(["IT", "Software"], groups[0].IndustryNames);
    }

    [Fact]
    public void CorrelatedIndustryGroupOptions_falls_back_on_malformed_json()
    {
        var options = new CorrelatedIndustryGroupOptions
        {
            GroupsJson = "not valid json"
        };

        var groups = options.GetGroups();

        Assert.Equal(3, groups.Count); // defaults
    }

    // ─── InMemoryPositionRepository (test double) ───────────────────────────────

    /// <summary>
    /// In-memory test double for <see cref="IPositionRepository"/>.
    /// Stores positions in a dictionary; supports the full interface.
    /// </summary>
    internal sealed class InMemoryPositionRepository : IPositionRepository
    {
        private readonly Dictionary<Guid, PositionDocument> _store = new();

        public void AddPosition(PositionDocument position)
        {
            _store[position.PositionId] = position;
        }

        public Task<PositionDocument?> GetByIdAsync(Guid positionId, CancellationToken ct = default)
        {
            _store.TryGetValue(positionId, out var doc);
            return Task.FromResult<PositionDocument?>(doc);
        }

        public Task CreateAsync(PositionDocument position, CancellationToken ct = default)
        {
            _store[position.PositionId] = position;
            return Task.CompletedTask;
        }

        public Task<PositionOccResult> UpdateWithOccAsync(
            PositionDocument position, long expectedVersion, long? fencingToken = null, CancellationToken ct = default)
        {
            if (!_store.TryGetValue(position.PositionId, out var current))
                return Task.FromResult(new PositionOccResult(false, 0));

            if (current.Version != expectedVersion)
                return Task.FromResult(new PositionOccResult(false, 0));

            var newVersion = expectedVersion + 1;
            position.Version = newVersion;
            _store[position.PositionId] = position;
            return Task.FromResult(new PositionOccResult(true, newVersion));
        }

        public Task<List<PositionDocument>> GetNonTerminalAsync(CancellationToken ct = default)
        {
            return Task.FromResult(_store.Values
                .Where(p => p.State != PositionState.Closed && p.State != PositionState.Rejected)
                .ToList());
        }

        public Task<List<PositionDocument>> GetNonTerminalByUserIdAsync(
            string userId, CancellationToken ct = default)
        {
            return Task.FromResult(_store.Values
                .Where(p => p.UserId == userId
                         && p.State != PositionState.Closed
                         && p.State != PositionState.Rejected)
                .ToList());
        }

        public Task<List<PositionDocument>> GetByUserIdAndStateAsync(
            string userId, PositionState state, CancellationToken ct = default)
        {
            return Task.FromResult(_store.Values
                .Where(p => p.UserId == userId && p.State == state)
                .ToList());
        }
    }
}
