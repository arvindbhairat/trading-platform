using Microsoft.Extensions.DependencyInjection;

namespace SignalStack.Notifications.TelegramBot;

/// <summary>
/// DI registration extensions for the Telegram Bot domain.
/// REQ-NOTIFY-015: bot management services and endpoints.
/// REQ-NOTIFY-016: linking flow services.
/// </summary>
public static class TelegramBotExtensions
{
    public static IServiceCollection AddTelegramBotServices(
        this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ── Configuration ──────────────────────────────────────────────────
        services
            .AddOptions<TelegramBotOptions>()
            .BindConfiguration(TelegramBotOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        // ── Token store ────────────────────────────────────────────────────
        // In production, replace with KeyVaultBotTokenStore.
        // The LocalDevBotTokenStore writes to the bootstrap key vault snapshot
        // file, which is read by the NDJ's ResolveBotTokenAsync fallback.
        services.AddSingleton<ITelegramBotTokenStore, LocalDevBotTokenStore>();

        // ── Named HttpClient for Telegram API calls ────────────────────────
        services.AddHttpClient("TelegramBotApi", client =>
        {
            client.DefaultRequestHeaders.Add("Accept", "application/json");
        });

        // ── Core services ──────────────────────────────────────────────────
        services.AddSingleton<TelegramBotService>();
        services.AddSingleton<TelegramLinkingService>();

        return services;
    }
}
