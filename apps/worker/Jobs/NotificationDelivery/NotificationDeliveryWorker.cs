using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignalStack.Worker.Observability;

namespace SignalStack.Worker.Jobs.NotificationDelivery;

/// <summary>
/// BackgroundService for the Notification Delivery Job (NDJ).
///
/// Polls for pending notification records on a configurable interval and
/// dispatches them via the Telegram Bot API through
/// <see cref="NotificationDeliveryService"/>.
///
/// REQ-NOTIFY-007:  NDJ is the sole Telegram dispatcher.
/// REQ-NOTIFY-008:  retry with exponential back-off; 429 Retry-After handling.
/// REQ-NOTIFY-022a: global-disable break-glass.
/// </summary>
internal sealed class NotificationDeliveryWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<NotificationDeliveryOptions> _options;
    private readonly ILogger<NotificationDeliveryWorker> _logger;

    public NotificationDeliveryWorker(
        IServiceProvider serviceProvider,
        IOptions<NotificationDeliveryOptions> options,
        ILogger<NotificationDeliveryWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds);

        _logger.LogInformation(
            "NDJ worker started (poll interval: {Interval}s).",
            _options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                int processedCount;

                using (var scope = _serviceProvider.CreateScope())
                {
                    var deliveryService = scope.ServiceProvider
                        .GetRequiredService<NotificationDeliveryService>();

                    processedCount = await deliveryService
                        .ExecuteDeliveryCycleAsync(stoppingToken);
                }

                // Only emit the NDJ trace span when actual work was done.
                // Most poll cycles find zero pending notifications, so this
                // eliminates ~8,000 redundant spans/day (~13% of Worker
                // event volume). Metrics datapoints are still recorded on
                // every cycle so cycle-timing trends remain observable.
                if (processedCount > 0)
                {
                    using var activity = WorkerTelemetry.ActivitySource
                        .StartActivity("NDJ.poll_cycle");
                    activity?.SetTag("job.type", "NDJ");
                    activity?.SetTag("ndj.processed_count", processedCount);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NDJ worker error during delivery cycle.");

                // Emit span on error so failures remain observable.
                using var activity = WorkerTelemetry.ActivitySource
                    .StartActivity("NDJ.poll_cycle");
                activity?.SetTag("job.type", "NDJ");
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                activity?.AddException(ex);
            }

            WorkerTelemetry.NdjDeliveryDurationMs.Record(sw.Elapsed.TotalMilliseconds);
            await Task.Delay(pollInterval, stoppingToken);
        }
    }
}
