using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var deliveryService = scope.ServiceProvider
                    .GetRequiredService<NotificationDeliveryService>();

                await deliveryService.ExecuteDeliveryCycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "NDJ worker error during delivery cycle.");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }
}
