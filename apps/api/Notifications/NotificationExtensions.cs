namespace SignalStack.Api.Notifications;

/// <summary>
/// DI registration extensions for the Notifications domain.
/// Registers the repository, writer, and supporting services.
/// REQ-NOTIFY-004: synchronous persistence before Telegram delivery.
/// REQ-NOTIFY-007: producers write to notifications collection only.
/// </summary>
public static class NotificationExtensions
{
    public static IServiceCollection AddNotificationServices(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<INotificationRepository, MongoNotificationRepository>();
        services.AddSingleton<INotificationWriter, NotificationWriter>();

        return services;
    }
}
