using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SignalStack.Notifications.TelegramBot;

/// <summary>
/// Local-development implementation of <see cref="ITelegramBotTokenStore"/>
/// that persists the bot token to the bootstrap key vault snapshot file
/// (<c>bootstrap/keyvault.local.json</c>).
///
/// REQ-NOTIFY-015: in production this is replaced by a Key Vault-backed
/// implementation using the application's managed identity.
/// </summary>
public sealed class LocalDevBotTokenStore : ITelegramBotTokenStore
{
    private const string BootstrapFileName = "bootstrap/keyvault.local.json";
    private const string TokenKey = "TelegramBotToken";

    private readonly string _bootstrapPath;
    private readonly ILogger<LocalDevBotTokenStore> _logger;

    public LocalDevBotTokenStore(IHostEnvironment env, ILogger<LocalDevBotTokenStore> logger)
    {
        _bootstrapPath = Path.Combine(env.ContentRootPath, BootstrapFileName);
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<string?> GetTokenAsync(CancellationToken ct = default)
    {
        try
        {
            if (!File.Exists(_bootstrapPath))
                return null;

            var json = await File.ReadAllTextAsync(_bootstrapPath, ct);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty(TokenKey, out var token))
                return token.GetString();
        }
        catch (Exception ex)
        {
            _logger.LogTrace(ex, "Could not read Telegram bot token from bootstrap snapshot.");
        }

        return Environment.GetEnvironmentVariable("TELEGRAM_BOT_TOKEN");
    }

    /// <inheritdoc />
    public async Task SetTokenAsync(string token, string replacedByUserId, CancellationToken ct = default)
    {
        try
        {
            var dir = Path.GetDirectoryName(_bootstrapPath);
            if (dir is not null && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            Dictionary<string, string> current;
            if (File.Exists(_bootstrapPath))
            {
                var json = await File.ReadAllTextAsync(_bootstrapPath, ct);
                current = JsonSerializer.Deserialize<Dictionary<string, string>>(json)
                    ?? new Dictionary<string, string>();
            }
            else
            {
                current = new Dictionary<string, string>();
            }

            current[TokenKey] = token;

            var options = new JsonSerializerOptions { WriteIndented = true };
            var result = JsonSerializer.Serialize(current, options);
            await File.WriteAllTextAsync(_bootstrapPath, result, ct);

            _logger.LogInformation(
                "LocalDevBotTokenStore: Telegram bot token updated by {UserId}.",
                replacedByUserId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to write Telegram bot token to bootstrap snapshot.");
            throw;
        }
    }
}
