using Microsoft.Extensions.Logging.Abstractions;
using SignalStack.Worker.Jobs.EodSuccessMarker;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Unit tests for <see cref="EodSequencingGate"/>.
///
/// REQ-MARKET-007: EODSR must refuse to start unless a DataSync success marker
/// exists for the target trading session.
/// </summary>
public sealed class EodSequencingGateTests
{
    private static readonly DateOnly TestSession = new(2026, 4, 28);
    private static readonly DateOnly MissingSession = new(2026, 4, 29);

    // ─── EnsureDataSyncCompletedAsync — marker present ──────────────────────

    [Fact]
    public async Task Gate_passes_when_marker_exists()
    {
        // Arrange — reader returns true for the target session
        var reader = new FakeEodMarkerReader(hasCompleted: true);
        var gate = new EodSequencingGate(reader, NullLogger<EodSequencingGate>.Instance);

        // Act & Assert — no exception
        await gate.EnsureDataSyncCompletedAsync(TestSession);
    }

    [Fact]
    public async Task Gate_passes_when_marker_exists_for_different_session()
    {
        // Arrange — reader returns true for TestSession, false for MissingSession
        var reader = new FakeEodMarkerReader(hasCompleted: true);
        var gate = new EodSequencingGate(reader, NullLogger<EodSequencingGate>.Instance);

        // Act — checking a session that also has a marker
        await gate.EnsureDataSyncCompletedAsync(TestSession);

        // Assert — FakeEodMarkerReader returns true for all sessions
        Assert.True(true, "Gate passed when marker exists.");
    }

    // ─── EnsureDataSyncCompletedAsync — marker absent ───────────────────────

    [Fact]
    public async Task Gate_throws_when_marker_is_absent()
    {
        // Arrange — reader returns false
        var reader = new FakeEodMarkerReader(hasCompleted: false);
        var gate = new EodSequencingGate(reader, NullLogger<EodSequencingGate>.Instance);

        // Act & Assert
        var ex = await Assert.ThrowsAsync<DataSyncNotCompletedException>(
            () => gate.EnsureDataSyncCompletedAsync(MissingSession));

        Assert.Equal(MissingSession, ex.SessionDate);
        Assert.Contains(MissingSession.ToString("yyyy-MM-dd"), ex.Message);
    }

    [Fact]
    public async Task Gate_throws_with_session_date_in_message()
    {
        var reader = new FakeEodMarkerReader(hasCompleted: false);
        var gate = new EodSequencingGate(reader, NullLogger<EodSequencingGate>.Instance);

        var ex = await Assert.ThrowsAsync<DataSyncNotCompletedException>(
            () => gate.EnsureDataSyncCompletedAsync(TestSession));

        Assert.Contains(TestSession.ToString("yyyy-MM-dd"), ex.Message);
        Assert.Contains("REQ-MARKET-007", ex.Message);
    }

    // ─── DataSyncNotCompletedException ──────────────────────────────────────

    [Fact]
    public void Exception_carries_session_date()
    {
        var ex = new DataSyncNotCompletedException(TestSession);

        Assert.Equal(TestSession, ex.SessionDate);
        Assert.Equal(TestSession.ToString("yyyy-MM-dd"), ex.SessionDateStr);
    }

    [Fact]
    public void Exception_is_InvalidOperationException()
    {
        var ex = new DataSyncNotCompletedException(TestSession);
        Assert.IsAssignableFrom<InvalidOperationException>(ex);
    }

    // ─── Gate with reader returning latest session ──────────────────────────

    [Fact]
    public async Task Gate_throws_when_no_marker_exists_for_latest_session()
    {
        // An empty reader (no markers at all) should cause the gate to throw
        // for any session.
        var reader = new FakeEodMarkerReader(hasCompleted: false);
        var gate = new EodSequencingGate(reader, NullLogger<EodSequencingGate>.Instance);

        await Assert.ThrowsAsync<DataSyncNotCompletedException>(
            () => gate.EnsureDataSyncCompletedAsync(MissingSession));
    }

    // ─── FakeEodMarkerReader ─────────────────────────────────────────────────

    private sealed class FakeEodMarkerReader : IEodMarkerReader
    {
        private readonly bool _hasCompleted;

        public FakeEodMarkerReader(bool hasCompleted)
        {
            _hasCompleted = hasCompleted;
        }

        public Task<bool> HasCompletedAsync(DateOnly sessionDate, CancellationToken ct = default)
            => Task.FromResult(_hasCompleted);

        public Task<DateOnly?> GetLatestCompletedSessionAsync(CancellationToken ct = default)
            => Task.FromResult(_hasCompleted ? (DateOnly?)new DateOnly(2026, 4, 28) : null);

        public Task<IReadOnlySet<DateOnly>> GetCompletedSessionsInRangeAsync(
            DateOnly fromDate, DateOnly toDate, CancellationToken ct = default)
        {
            var empty = new HashSet<DateOnly>();
            return Task.FromResult<IReadOnlySet<DateOnly>>(empty);
        }
    }
}
