namespace SignalStack.Api.Pld;

/// <summary>
/// Registers PLD WebSocket lease services and the in-memory connection manager.
/// REQ-SESSION-014.
/// </summary>
public static class PldExtensions
{
    public static IServiceCollection AddPldWebSocketServices(this IServiceCollection services)
    {
        services.AddSingleton<IPldWebSocketLease, RedisPldWebSocketLease>();
        services.AddSingleton<PldConnectionManager>();
        return services;
    }
}
