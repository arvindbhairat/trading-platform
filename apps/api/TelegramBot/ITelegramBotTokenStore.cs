namespace SignalStack.Api.TelegramBot;

/// <summary>
/// Abstraction for storing and retrieving the Telegram bot token.
///
/// In production the token is stored in Azure Key Vault per REQ-NOTIFY-015.
/// Local development uses a file-based fallback.
/// </summary>
public interface ITelegramBotTokenStore
{
    /// <summary>Returns the current active bot token, or null if none is configured.</summary>
    Task<string?> GetTokenAsync(CancellationToken ct = default);

    /// <summary>
    /// Stores a new bot token. In production this writes to Azure Key Vault;
    /// in local development it writes to a bootstrap configuration file.
    /// </summary>
    Task SetTokenAsync(string token, string replacedByUserId, CancellationToken ct = default);
}
