using System.Net.Sockets;
using StackExchange.Redis;

namespace SignalStack.Api.Pld;

/// <summary>
/// Redis-backed PLD WebSocket lease.
///
/// Key pattern: <c>session:fyers:pld:{userId}</c>
/// Value: unique instance ID (GUID string) of the connection holding the lease.
/// TTL: <c>sys_config.session.fyers.lease_ttl_seconds</c> (default 1800).
///
/// Acquisition uses SET NX to claim a free lease. On conflict the lease is
/// overwritten (latest-tab-wins) and the previous holder is returned so the
/// caller can evict the old connection. Refresh uses a Lua script that checks
/// the caller still owns the key before extending TTL.
///
/// REQ-SESSION-014(a)/(b)/(d).
/// </summary>
internal sealed class RedisPldWebSocketLease : IPldWebSocketLease
{
    private static readonly LuaScript RefreshScript = LuaScript.Prepare("""
        if redis.call('GET', @leaseKey) == @instanceId then
            return redis.call('PEXPIRE', @leaseKey, @ttlMs)
        end
        return 0
        """);

    private static readonly LuaScript ReleaseScript = LuaScript.Prepare("""
        if redis.call('GET', @leaseKey) == @instanceId then
            return redis.call('DEL', @leaseKey)
        end
        return 0
        """);

    private readonly ILogger<RedisPldWebSocketLease> _logger;
    private readonly IConfiguration _configuration;

    public RedisPldWebSocketLease(
        ILogger<RedisPldWebSocketLease> logger,
        IConfiguration configuration)
    {
        _logger = logger;
        _configuration = configuration;
    }

    public async Task<PldLeaseAcquireResult> TryAcquireAsync(
        string userId, string instanceId, CancellationToken ct)
    {
        var leaseKey = LeaseKey(userId);
        var ttl = GetLeaseTtl();

        try
        {
            var db = await GetDatabaseAsync();
            if (db is null)
                return PldLeaseAcquireResult.Unavailable("redis_connection_string_missing");

            // REQ-SESSION-014(a): try to acquire with SET NX EX.
            var acquired = await db.StringSetAsync(
                leaseKey, instanceId, ttl, When.NotExists, CommandFlags.DemandMaster);

            if (acquired)
                return PldLeaseAcquireResult.Acquired();

            // REQ-SESSION-014(b): conflict — read the previous holder.
            var previous = await db.StringGetAsync(leaseKey, CommandFlags.DemandMaster);
            var previousId = previous.HasValue ? previous.ToString() : null;

            // Overwrite with the new instance (latest-tab-wins).
            await db.StringSetAsync(leaseKey, instanceId, ttl, flags: CommandFlags.DemandMaster);

            _logger.LogInformation(
                "PLD lease for user {UserId} transferred from {PreviousInstance} to {NewInstance}",
                userId, previousId, instanceId);

            return PldLeaseAcquireResult.Evicted(previousId ?? "");
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or SocketException)
        {
            _logger.LogWarning(ex, "PLD WebSocket lease acquire could not reach Redis for user {UserId}.", userId);
            return PldLeaseAcquireResult.Unavailable(ex.GetType().Name);
        }
    }

    public async Task<PldLeaseRefreshResult> TryRefreshAsync(
        string userId, string instanceId, CancellationToken ct)
    {
        var leaseKey = LeaseKey(userId);
        var ttlMs = (long)GetLeaseTtl().TotalMilliseconds;

        try
        {
            var db = await GetDatabaseAsync();
            if (db is null)
                return PldLeaseRefreshResult.Unavailable("redis_connection_string_missing");

            var result = (int)await db.ScriptEvaluateAsync(
                RefreshScript,
                new { leaseKey, instanceId, ttlMs },
                flags: CommandFlags.DemandMaster);

            return result == 1
                ? PldLeaseRefreshResult.Refreshed()
                : PldLeaseRefreshResult.LostLease();
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or SocketException)
        {
            _logger.LogWarning(ex, "PLD WebSocket lease refresh could not reach Redis for user {UserId}.", userId);
            return PldLeaseRefreshResult.Unavailable(ex.GetType().Name);
        }
    }

    public async Task ReleaseAsync(string userId, string instanceId, CancellationToken ct)
    {
        var leaseKey = LeaseKey(userId);

        try
        {
            var db = await GetDatabaseAsync();
            if (db is null)
                return;

            await db.ScriptEvaluateAsync(
                ReleaseScript,
                new { leaseKey, instanceId },
                flags: CommandFlags.DemandMaster);
        }
        catch (Exception ex) when (ex is RedisConnectionException or RedisTimeoutException or SocketException)
        {
            _logger.LogWarning(ex, "PLD WebSocket lease release could not reach Redis for user {UserId}.", userId);
        }
    }

    private static string LeaseKey(string userId) => $"session:fyers:pld:{userId}";

    private TimeSpan GetLeaseTtl()
    {
        var seconds = _configuration.GetValue<int>("Pld:LeaseTtlSeconds");
        return seconds > 0 ? TimeSpan.FromSeconds(seconds) : TimeSpan.FromSeconds(1800);
    }

    private async Task<IDatabase?> GetDatabaseAsync()
    {
        var connectionString = _configuration.GetConnectionString("Redis")
            ?? _configuration["Redis:ConnectionString"];

        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        var multiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
        return multiplexer.GetDatabase();
    }
}
