using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace SignalStack.Worker.Jobs.NotificationDelivery;

/// <summary>
/// DI registration extensions for the Notification Delivery Job (NDJ).
///
/// Usage in <c>Program.cs</c>:
/// <code>
/// builder.Services.AddNotificationDelivery(builder.Configuration);
/// </code>
///
/// Registers:
/// - <see cref="NotificationDeliveryOptions"/> bound to the <c>NotificationDelivery</c> config section
/// - <see cref="NotificationDeliveryService"/> as a transient service
/// - <see cref="NotificationDeliveryWorker"/> as a hosted BackgroundService
///
/// REQ-NOTIFY-007: NDJ is the sole Telegram dispatcher.
/// REQ-NOTIFY-008: retry with exponential back-off; 429 Retry-After handling.
/// REQ-NOTIFY-019: dirty-link auto-disable after threshold.
/// REQ-NOTIFY-022a: global-disable break-glass flag.
/// REQ-NOTIFY-023: idempotent delivery via telegram_message_id.
/// </summary>
public static class NotificationDeliveryExtensions
{
    /// <summary>
    /// Register Notification Delivery Job services.
    /// </summary>
    public static IServiceCollection AddNotificationDelivery(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        // ── Step 1: Bind configuration options ─────────────────────────────
        services
            .AddOptions<NotificationDeliveryOptions>()
            .BindConfiguration(NotificationDeliveryOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Step 2: Register the core delivery service ──────────────────────
        services.AddTransient<NotificationDeliveryService>();

        // ── Step 3: Register named HttpClient for Telegram Bot API ──────────
        services.AddHttpClient("TelegramBotApi", client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // ── Step 4: Register the BackgroundService worker ───────────────────
        services.AddHostedService<NotificationDeliveryWorker>();

        return services;
    }
}
