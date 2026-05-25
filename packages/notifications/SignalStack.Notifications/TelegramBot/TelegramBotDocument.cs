using MongoDB.Bson;
using MongoDB.Bson.Serialization.Attributes;

namespace SignalStack.Notifications.TelegramBot;

/// <summary>
/// Admin-provisioned Telegram bot configuration record stored in the
/// MongoDB <c>notification_bots</c> collection.
///
/// The platform operates with a single shared bot at any one time.
/// Each record stores the bot's verified username, the bot token
/// Key Vault reference (never stored inline), active state, and
/// provisioning metadata.
///
/// A new record is written when the admin replaces the bot token
/// (REQ-NOTIFY-015); the previous record is retained for audit
/// purposes with its active flag set to false.
///
/// REQ-NOTIFY-015, REQ-NOTIFY-022a.
/// </summary>
public sealed class TelegramBotDocument
{
    [BsonId]
    public ObjectId Id { get; init; }

    /// <summary>Verified bot username returned by Telegram getMe.</summary>
    [BsonElement("bot_username")]
    public required string BotUsername { get; set; }

    /// <summary>
    /// Key Vault secret reference for the bot token.
    /// Never stores the token value inline.
    /// </summary>
    [BsonElement("token_key_vault_ref")]
    [BsonIgnoreIfNull]
    public string? TokenKeyVaultRef { get; set; }

    /// <summary>True when this is the currently active bot configuration.</summary>
    [BsonElement("is_active")]
    public bool IsActive { get; set; }

    /// <summary>User ID of the admin who provisioned this bot.</summary>
    [BsonElement("provisioned_by_user_id")]
    public required string ProvisionedByUserId { get; init; }

    /// <summary>UTC timestamp when this record was created.</summary>
    [BsonElement("provisioned_at")]
    public required DateTime ProvisionedAt { get; init; }

    /// <summary>UTC timestamp of the most recent token replacement.</summary>
    [BsonElement("last_token_replaced_at")]
    [BsonIgnoreIfNull]
    public DateTime? LastTokenReplacedAt { get; set; }

    /// <summary>UTC timestamp of the last successful Telegram delivery via this bot.</summary>
    [BsonElement("last_successful_delivery_at")]
    [BsonIgnoreIfNull]
    public DateTime? LastSuccessfulDeliveryAt { get; set; }

    /// <summary>UTC timestamp of the last delivery health check.</summary>
    [BsonElement("last_delivery_check_at")]
    [BsonIgnoreIfNull]
    public DateTime? LastDeliveryCheckAt { get; set; }

    /// <summary>
    /// ObjectId of the bot record that this record replaced.
    /// Null for the first-ever bot record.
    /// </summary>
    [BsonElement("previous_bot_id")]
    [BsonIgnoreIfNull]
    public ObjectId? PreviousBotId { get; init; }
}
