using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SignalStack.Migrations;

/// <summary>
/// Runs pending MongoDB migrations at application startup (REQ-MIGRATION-002).
/// The migration runner is idempotent — re-running produces no changes once a
/// migration is recorded in the <c>_migrations</c> tracking collection.
/// </summary>
public sealed class MongoMigrationHostedService : IHostedService
{
    private readonly MongoMigrationRunner _runner;
    private readonly ILogger<MongoMigrationHostedService> _logger;

    public MongoMigrationHostedService(
        MongoMigrationRunner runner,
        ILogger<MongoMigrationHostedService> logger)
    {
        _runner = runner;
        _logger = logger;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Running MongoDB migrations at startup");
        try
        {
            await _runner.RunAsync(cancellationToken);
            _logger.LogInformation("MongoDB migrations completed successfully");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "MongoDB migration failed — startup aborted");
            throw;
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
