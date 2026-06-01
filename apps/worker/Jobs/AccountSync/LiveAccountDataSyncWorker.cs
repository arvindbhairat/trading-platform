using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignalStack.Domain.Admin;
using SignalStack.Storage.Admin;
using SignalStack.Worker.Observability;

namespace SignalStack.Worker.Jobs.AccountSync;

/// <summary>
/// BackgroundService for the Live Account Data Sync (LADS) job.
///
/// Polls on a configurable interval and delegates to
/// <see cref="LiveAccountDataSyncService"/> for per-user intraday
/// account sync during NSE market hours.
///
/// REQ-PORT-019:  intraday recurring sync on configurable interval.
/// REQ-PORT-021a: cycle-level abort tracking + graduated suspension.
/// </summary>
internal sealed class LiveAccountDataSyncWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<AccountSyncOptions> _options;
    private readonly ILogger<LiveAccountDataSyncWorker> _logger;

    public LiveAccountDataSyncWorker(
        IServiceProvider serviceProvider,
        IOptions<AccountSyncOptions> options,
        ILogger<LiveAccountDataSyncWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds);

        _logger.LogInformation(
            "LADS worker started (poll interval: {Interval}s).",
            _options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var activity = WorkerTelemetry.ActivitySource.StartActivity("LADS.poll_cycle");
            activity?.SetTag("job.type", "LADS");
            var sw = Stopwatch.StartNew();
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var syncService = scope.ServiceProvider
                    .GetRequiredService<LiveAccountDataSyncService>();
                var calendar = scope.ServiceProvider
                    .GetRequiredService<ITradingCalendarRepository>();

                await syncService.ExecuteCycleAsync(calendar, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "LADS worker error during account sync cycle.");
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);
            }

            WorkerTelemetry.LadsSyncDurationMs.Record(sw.Elapsed.TotalMilliseconds);
            await Task.Delay(pollInterval, stoppingToken);
        }
    }
}
