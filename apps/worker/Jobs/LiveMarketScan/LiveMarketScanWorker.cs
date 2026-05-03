using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignalStack.Api.Admin;

namespace SignalStack.Worker.Jobs.LiveMarketScan;

/// <summary>
/// BackgroundService for the Live Market Data Scan (LMDS) job.
///
/// Polls on a configurable interval during NSE market hours and delegates
/// to <see cref="LiveMarketScanService"/> for per-cycle price + level evaluation.
///
/// REQ-STOP-006:   continuous intraday price monitoring during market hours.
/// REQ-STOP-006a:  only runs during NSE market hours per internal calendar.
/// </summary>
internal sealed class LiveMarketScanWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<LiveMarketScanOptions> _options;
    private readonly ILogger<LiveMarketScanWorker> _logger;

    public LiveMarketScanWorker(
        IServiceProvider serviceProvider,
        IOptions<LiveMarketScanOptions> options,
        ILogger<LiveMarketScanWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds);

        _logger.LogInformation(
            "LMDS worker started (poll interval: {Interval}s).",
            _options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var scanService = scope.ServiceProvider
                    .GetRequiredService<LiveMarketScanService>();
                var calendar = scope.ServiceProvider
                    .GetRequiredService<ITradingCalendarRepository>();

                // REQ-STOP-006a: only run during NSE market hours.
                if (!await IsMarketHoursAsync(calendar, stoppingToken))
                {
                    _logger.LogTrace("LMDS: outside NSE market hours. Skipping cycle.");
                    await Task.Delay(pollInterval, stoppingToken);
                    continue;
                }

                var result = await scanService.ExecuteCycleAsync(stoppingToken);

                if (result.PositionsEvaluated > 0)
                {
                    _logger.LogInformation(
                        "LMDS: cycle complete — {Positions} positions, {Symbols} symbols, " +
                        "{Breaches} breaches, {Stale} stale quotes in {ElapsedMs} ms.",
                        result.PositionsEvaluated,
                        result.SymbolsQuoted,
                        result.BreachesDetected,
                        result.StaleQuoteCount,
                        result.Elapsed.TotalMilliseconds);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LMDS worker error during scan cycle.");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }

    /// <summary>
    /// Checks whether the current UTC time falls within NSE market hours
    /// according to the internal trading calendar.
    /// REQ-STOP-006a: LMDS runs only during NSE market hours.
    /// REQ-CALENDAR-003: calendar is the shared authority for session timing.
    /// </summary>
    internal static async Task<bool> IsMarketHoursAsync(
        ITradingCalendarRepository calendar, CancellationToken ct)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var sessions = await calendar.GetSessionsAsync(
            fromDate: today.ToString("yyyy-MM-dd"),
            toDate: today.ToString("yyyy-MM-dd"),
            ct: ct);

        foreach (var session in sessions)
        {
            if (!DateOnly.TryParse(session.SessionDate, out var sessionDate)
                || sessionDate != today)
                continue;

            if (TimeSpan.TryParse(session.SessionStartTime, out var startTime)
                && TimeSpan.TryParse(session.SessionEndTime, out var endTime))
            {
                // Session times are stored as IST (UTC+5:30).
                var nowUtc = DateTime.UtcNow;
                var todayUtc = nowUtc.Date;
                var sessionStartUtc = todayUtc.Add(startTime).AddHours(-5).AddMinutes(-30);
                var sessionEndUtc = todayUtc.Add(endTime).AddHours(-5).AddMinutes(-30);

                return nowUtc >= sessionStartUtc && nowUtc <= sessionEndUtc;
            }
        }

        return false;
    }
}
