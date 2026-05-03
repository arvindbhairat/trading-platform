namespace SignalStack.Api.Push;

/// <summary>
/// DI registration for push notification WebSocket services.
/// REQ-NFR-013: WebSocket fan-out for real-time event delivery.
/// REQ-DATA-006a(c)(i): degraded fallback when Redis is unreachable.
/// </summary>
public static class PushExtensions
{
    public static IServiceCollection AddPushNotificationServices(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<PushConnectionManager>();
        services.AddHostedService<PushFanOutService>();

        return services;
    }
}
