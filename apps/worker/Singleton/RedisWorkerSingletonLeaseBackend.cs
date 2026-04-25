using System.Net.Sockets;
using StackExchange.Redis;

namespace SignalStack.Worker.Singleton;

internal sealed class RedisWorkerSingletonLeaseBackend : IWorkerSingletonLeaseBackend
{
  private static readonly LuaScript RefreshScript = LuaScript.Prepare("""
    if redis.call('GET', @leaseKey) == @instanceId then
      return redis.call('PEXPIRE', @leaseKey, @ttlMs)
    end
    return 0
    """);

  private readonly ILogger<RedisWorkerSingletonLeaseBackend> _logger;
  private readonly IConfiguration _configuration;

  public RedisWorkerSingletonLeaseBackend(
    ILogger<RedisWorkerSingletonLeaseBackend> logger,
    IConfiguration configuration)
  {
    _logger = logger;
    _configuration = configuration;
  }

  public async Task<WorkerLeaseAcquireResult> TryAcquireAsync(
    string leaseKey,
    string instanceId,
    TimeSpan leaseTtl,
    CancellationToken cancellationToken)
  {
    try
    {
      var database = await GetDatabaseAsync();
      if (database is null)
      {
        return WorkerLeaseAcquireResult.Unavailable("redis_connection_string_missing");
      }

      var acquired = await database.StringSetAsync(
        leaseKey,
        instanceId,
        leaseTtl,
        When.NotExists,
        CommandFlags.DemandMaster);

      if (acquired)
      {
        return WorkerLeaseAcquireResult.Acquired();
      }

      var holder = await database.StringGetAsync(leaseKey, CommandFlags.DemandMaster);
      return WorkerLeaseAcquireResult.Conflict(holder.HasValue ? holder.ToString() : null);
    }
    catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or SocketException)
    {
      _logger.LogWarning(ex, "Worker singleton lease backend could not reach Redis.");
      return WorkerLeaseAcquireResult.Unavailable(ex.GetType().Name);
    }
  }

  public async Task<WorkerLeaseRefreshResult> TryRefreshAsync(
    string leaseKey,
    string instanceId,
    TimeSpan leaseTtl,
    CancellationToken cancellationToken)
  {
    try
    {
      var database = await GetDatabaseAsync();
      if (database is null)
      {
        return WorkerLeaseRefreshResult.Unavailable("redis_connection_string_missing");
      }

      var result = (int)await database.ScriptEvaluateAsync(
        RefreshScript,
        new
        {
          leaseKey,
          instanceId,
          ttlMs = (long)leaseTtl.TotalMilliseconds
        });

      return result == 1
        ? WorkerLeaseRefreshResult.Refreshed()
        : WorkerLeaseRefreshResult.LostLease();
    }
    catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or SocketException)
    {
      _logger.LogWarning(ex, "Worker singleton lease refresh could not reach Redis.");
      return WorkerLeaseRefreshResult.Unavailable(ex.GetType().Name);
    }
  }

  private async Task<IDatabase?> GetDatabaseAsync()
  {
    var connectionString = _configuration.GetConnectionString("Redis")
      ?? _configuration["Redis:ConnectionString"];

    if (string.IsNullOrWhiteSpace(connectionString))
    {
      return null;
    }

    var multiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
    return multiplexer.GetDatabase();
  }
}
