using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SignalStack.Domain.Admin;
using SignalStack.Storage.Admin;

namespace SignalStack.Worker.Jobs.AdminFyersTokenCheck;

/// <summary>
/// BackgroundService for the Admin FYERS Token Validity Check job.
///
/// Polls on a configurable interval and delegates to
/// <see cref="AdminFyersTokenCheckService"/> for token validity evaluation.
///
/// REQ-NOTIFY-022:  daily 15:00 IST DataSync-window check.
/// REQ-NOTIFY-022b: pre-market 08:30 IST check.
/// </summary>
internal sealed class AdminFyersTokenCheckWorker : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IOptions<AdminFyersTokenCheckOptions> _options;
    private readonly ILogger<AdminFyersTokenCheckWorker> _logger;

    public AdminFyersTokenCheckWorker(
        IServiceProvider serviceProvider,
        IOptions<AdminFyersTokenCheckOptions> options,
        ILogger<AdminFyersTokenCheckWorker> logger)
    {
        _serviceProvider = serviceProvider;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var pollInterval = TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds);

        _logger.LogInformation(
            "AdminFyersTokenCheck worker started (poll interval: {Interval}s).",
            _options.Value.PollIntervalSeconds);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var checkService = scope.ServiceProvider
                    .GetRequiredService<AdminFyersTokenCheckService>();
                var calendar = scope.ServiceProvider
                    .GetRequiredService<ITradingCalendarRepository>();

                await checkService.TryRunChecksAsync(calendar, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AdminFyersTokenCheck worker error during token check cycle.");
            }

            await Task.Delay(pollInterval, stoppingToken);
        }
    }
}
