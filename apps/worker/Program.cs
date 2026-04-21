using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHostedService<Worker>();

var host = builder.Build();
await host.RunAsync();

internal sealed class Worker : BackgroundService
{
  private readonly ILogger<Worker> _logger;

  public Worker(ILogger<Worker> logger)
  {
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("SignalStack.Worker started.");

    while (!stoppingToken.IsCancellationRequested)
    {
      await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
      _logger.LogInformation("SignalStack.Worker heartbeat.");
    }
  }
}

