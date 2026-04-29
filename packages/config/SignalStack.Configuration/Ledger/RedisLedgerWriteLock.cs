using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace SignalStack.Configuration.Ledger;

/// <summary>
/// Redis-backed implementation of <see cref="ILedgerWriteLock"/>.
///
/// <para>Redlock note (REQ-PORT-031b(a)): This implementation uses a single Redis node
/// (SET NX with TTL), which degrades Redlock to a simple exclusive lock.  In a single-node
/// deployment this is an accepted trade-off: the lock is not safe against Redis process failure
/// during a held lock, but provides correctness under normal conditions.  A three-node Redlock
/// deployment is required for production hardening; this will be addressed before broader rollout.</para>
///
/// <para>Fencing token (REQ-PORT-031b(b)): every successful acquisition increments a per-user
/// counter (ledger:write:fence:{userId}) and returns the new value.  MongoDB write filters must
/// include { "ledger_fence_token": { "$lte": FencingToken } }; zero-match means stale-write abort.</para>
///
/// <para>DB index (REQ-PORT-031b(c)): lock keys use <see cref="LedgerLockOptions.LockDbIndex"/>
/// (default 1), which must be configured with maxmemory-policy noeviction in Azure Cache for Redis.
/// Cache keys must reside in a different logical database index.</para>
/// </summary>
public sealed class RedisLedgerWriteLock : ILedgerWriteLock
{
  private static readonly LuaScript RenewScript = LuaScript.Prepare("""
    if redis.call('GET', @lockKey) == @token then
      return redis.call('PEXPIRE', @lockKey, @ttlMs)
    end
    return 0
    """);

  private static readonly LuaScript ReleaseScript = LuaScript.Prepare("""
    if redis.call('GET', @lockKey) == @token then
      return redis.call('DEL', @lockKey)
    end
    return 0
    """);

  private static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(250);

  private readonly ILogger<RedisLedgerWriteLock> _logger;
  private readonly IConfiguration _configuration;
  private readonly LedgerLockOptions _options;

  public RedisLedgerWriteLock(
    ILogger<RedisLedgerWriteLock> logger,
    IConfiguration configuration,
    IOptions<LedgerLockOptions> options)
  {
    _logger = logger;
    _configuration = configuration;
    _options = options.Value;
  }

  public async Task<LedgerLockAcquireResult> AcquireAsync(
    string userId,
    LedgerAcquireMode mode,
    CancellationToken cancellationToken)
  {
    var timeout = mode switch
    {
      LedgerAcquireMode.TryNonBlocking   => TimeSpan.Zero,
      LedgerAcquireMode.WaitFiveSeconds   => TimeSpan.FromSeconds(5),
      LedgerAcquireMode.WaitThirtySeconds => TimeSpan.FromSeconds(30),
      _                                   => TimeSpan.Zero
    };

    var lockKey = $"ledger:write:lock:{userId}";
    var fenceKey = $"ledger:write:fence:{userId}";
    var token = Guid.NewGuid().ToString("N");
    var ttl = TimeSpan.FromSeconds(_options.TtlSeconds);
    var deadline = DateTimeOffset.UtcNow + timeout;

    try
    {
      do
      {
        var db = await GetDatabaseAsync();
        if (db is null)
          return LedgerLockAcquireResult.Unavailable("redis_connection_string_missing");

        var acquired = await db.StringSetAsync(
          lockKey, token, ttl, When.NotExists, CommandFlags.DemandMaster);

        if (acquired)
        {
          var fencingToken = await db.StringIncrementAsync(fenceKey, flags: CommandFlags.DemandMaster);

          var handle = new LedgerLockHandle(
            fencingToken,
            ct => TryRenewCoreAsync(lockKey, token, ttl, ct),
            () => ReleaseCoreAsync(lockKey, token));

          return LedgerLockAcquireResult.Acquired(handle);
        }

        if (timeout == TimeSpan.Zero)
          break;

        await Task.Delay(RetryInterval, cancellationToken);
      }
      while (DateTimeOffset.UtcNow < deadline);

      return LedgerLockAcquireResult.Busy();
    }
    catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or SocketException)
    {
      _logger.LogWarning(ex, "Ledger write lock acquire could not reach Redis for user {UserId}.", userId);
      return LedgerLockAcquireResult.Unavailable(ex.GetType().Name);
    }
    catch (OperationCanceledException)
    {
      return LedgerLockAcquireResult.Busy();
    }
  }

  private async Task<bool> TryRenewCoreAsync(
    string lockKey, string token, TimeSpan ttl, CancellationToken cancellationToken)
  {
    try
    {
      var db = await GetDatabaseAsync();
      if (db is null) return false;

      var result = (int)await db.ScriptEvaluateAsync(
        RenewScript,
        new { lockKey, token, ttlMs = (long)ttl.TotalMilliseconds });

      return result == 1;
    }
    catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or SocketException)
    {
      _logger.LogWarning(ex, "Ledger write lock renewal could not reach Redis.");
      return false;
    }
  }

  private async Task ReleaseCoreAsync(string lockKey, string token)
  {
    try
    {
      var db = await GetDatabaseAsync();
      if (db is null) return;

      await db.ScriptEvaluateAsync(ReleaseScript, new { lockKey, token });
    }
    catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or SocketException)
    {
      _logger.LogWarning(ex, "Ledger write lock release could not reach Redis.");
    }
  }

  private async Task<IDatabase?> GetDatabaseAsync()
  {
    var connectionString = _configuration.GetConnectionString("Redis")
      ?? _configuration["Redis:ConnectionString"];

    if (string.IsNullOrWhiteSpace(connectionString))
      return null;

    var multiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
    return multiplexer.GetDatabase(_options.LockDbIndex);
  }
}
