using Microsoft.Extensions.Logging;

namespace SignalStack.Worker.Jobs.EodSuccessMarker;

/// <summary>
/// Sequencing gate that EODSR (and any other downstream consumer) calls before
/// starting its processing cycle.
///
/// REQ-MARKET-007: EODSR must refuse to start unless a DataSync success marker
/// exists for the target trading session. This gate encapsulates that check and
/// throws <see cref="DataSyncNotCompletedException"/> when the marker is absent.
/// </summary>
public sealed class EodSequencingGate
{
    private readonly IEodMarkerReader _reader;
    private readonly ILogger<EodSequencingGate> _logger;

    public EodSequencingGate(IEodMarkerReader reader, ILogger<EodSequencingGate> logger)
    {
        _reader = reader;
        _logger = logger;
    }

    /// <summary>
    /// Ensures that DataSync has completed for the specified trading session.
    /// </summary>
    /// <param name="sessionDate">The trading session date to check.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <exception cref="DataSyncNotCompletedException">
    /// Thrown when no DS success marker exists for <paramref name="sessionDate"/>.
    /// </exception>
    public async Task EnsureDataSyncCompletedAsync(DateOnly sessionDate, CancellationToken ct = default)
    {
        var hasMarker = await _reader.HasCompletedAsync(sessionDate, ct);

        if (!hasMarker)
        {
            _logger.LogError(
                "EODSR sequencing gate: DataSync has not completed for session {Session}. " +
                "EODSR cannot start until the DS success marker is present (REQ-MARKET-007).",
                sessionDate);

            throw new DataSyncNotCompletedException(sessionDate);
        }

        _logger.LogInformation(
            "EODSR sequencing gate: DataSync completion verified for session {Session}.",
            sessionDate);
    }
}
