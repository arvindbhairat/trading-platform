using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace SignalStack.Worker.Push;

/// <summary>
/// Redis-backed implementation of <see cref="IPushEventPublisher"/>.
///
/// Publishes push events to the <c>push:events</c> Redis channel for fan-out
/// to connected portal WebSocket clients.
///
/// REQ-DATA-006a(c)(i): when Redis is unreachable, logs a warning and returns
/// false. The caller has already persisted the event to MongoDB, so no data
/// loss occurs — the client will pick it up via polling on their next cycle.
/// </summary>
internal sealed class RedisPushEventPublisher : IPushEventPublisher, IDisposable
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<RedisPushEventPublisher> _logger;
    private readonly JsonSerializerOptions _jsonOptions;

    private ConnectionMultiplexer? _multiplexer;
    private bool _disposed;

    private const string PushChannel = "push:events";

    public bool IsConnected => _multiplexer?.IsConnected == true;

    public RedisPushEventPublisher(
        IConfiguration configuration,
        ILogger<RedisPushEventPublisher> logger)
    {
        _configuration = configuration;
        _logger = logger;

        _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            WriteIndented = false
        };
    }

    public async Task<bool> PublishAsync(
        string eventType,
        string userId,
        string? symbol = null,
        string? positionId = null,
        string? content = null,
        string? deepLink = null,
        decimal? price = null,
        string? levelType = null,
        string? notificationType = null,
        CancellationToken ct = default)
    {
        try
        {
            var multiplexer = await GetMultiplexerAsync();
            if (multiplexer is null)
            {
                _logger.LogWarning(
                    "Redis not available. Push event {EventType} for user {UserId} not published. " +
                    "REQ-DATA-006a(c)(i): client will pick up via polling.",
                    eventType, userId);
                return false;
            }

            var pushEvent = new
            {
                event_type = eventType,
                user_id = userId,
                occurred_at = DateTimeOffset.UtcNow.ToString("o"),
                symbol,
                position_id = positionId,
                content,
                deep_link = deepLink,
                price,
                level_type = levelType,
                notification_type = notificationType,
            };

            var json = JsonSerializer.Serialize(pushEvent, _jsonOptions);
            var subscriber = multiplexer.GetSubscriber();

            await subscriber.PublishAsync(
                new RedisChannel(PushChannel, RedisChannel.PatternMode.Literal),
                json,
                CommandFlags.FireAndForget);

            return true;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex,
                "Redis connection error publishing push event {EventType} for user {UserId}. " +
                "REQ-DATA-006a(c)(i): client will pick up via polling.",
                eventType, userId);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex,
                "Unexpected error publishing push event {EventType} for user {UserId}.",
                eventType, userId);
            return false;
        }
    }

    private async Task<ConnectionMultiplexer?> GetMultiplexerAsync()
    {
        if (_multiplexer?.IsConnected == true)
            return _multiplexer;

        var connectionString = _configuration.GetConnectionString("Redis")
            ?? _configuration["Redis:ConnectionString"];

        if (string.IsNullOrWhiteSpace(connectionString))
            return null;

        try
        {
            // Dispose old if reconnecting.
            if (_multiplexer is not null)
            {
                _multiplexer.Dispose();
                _multiplexer = null;
            }

            _multiplexer = await ConnectionMultiplexer.ConnectAsync(connectionString);
            return _multiplexer;
        }
        catch (RedisConnectionException ex)
        {
            _logger.LogWarning(ex, "Failed to connect to Redis for push event publisher.");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _multiplexer?.Dispose();
        _multiplexer = null;
    }
}
