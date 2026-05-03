using System.Net;
using System.Net.Http.Headers;
using SignalStack.Worker.Jobs.NotificationDelivery;
using Xunit;

namespace SignalStack.Worker.Tests;

/// <summary>
/// Unit tests for the Notification Delivery Job (NDJ).
///
/// REQ-NOTIFY-008: 429 Retry-After handling.
/// REQ-NOTIFY-019: dirty-link detection and auto-disable threshold.
/// REQ-NOTIFY-022a: global-disable break-glass.
/// REQ-NOTIFY-023: idempotent delivery via telegram_message_id.
/// </summary>
public sealed class NotificationDeliveryServiceTests
{
    // ─── Retry-After header parsing (REQ-NOTIFY-008) ─────────────────────────

    [Fact]
    public void ParseRetryAfter_returns_delta_seconds_when_RetryAfter_delta_set()
    {
        // Arrange
        var headers = new HttpResponseMessage(HttpStatusCode.TooManyRequests).Headers;
        headers.RetryAfter = new RetryConditionHeaderValue(TimeSpan.FromSeconds(30));

        // Act
        var seconds = InvokeParseRetryAfter(headers);

        // Assert
        Assert.Equal(30, seconds);
    }

    [Fact]
    public void ParseRetryAfter_returns_5_when_no_RetryAfter_header()
    {
        // Arrange
        var headers = new HttpResponseMessage(HttpStatusCode.TooManyRequests).Headers;

        // Act
        var seconds = InvokeParseRetryAfter(headers);

        // Assert
        Assert.Equal(5, seconds);
    }

    // ─── Chat unreachable error detection (REQ-NOTIFY-019) ───────────────────

    [Fact]
    public void IsChatUnreachableError_returns_true_for_blocked_bot()
    {
        var result = InvokeIsChatUnreachableError(
            "Forbidden: bot was blocked by the user");
        Assert.True(result);
    }

    [Fact]
    public void IsChatUnreachableError_returns_true_for_deactivated_user()
    {
        var result = InvokeIsChatUnreachableError(
            "Forbidden: user is deactivated");
        Assert.True(result);
    }

    [Fact]
    public void IsChatUnreachableError_returns_true_for_chat_not_found()
    {
        var result = InvokeIsChatUnreachableError(
            "chat not found");
        Assert.True(result);
    }

    [Fact]
    public void IsChatUnreachableError_returns_false_for_other_errors()
    {
        var result = InvokeIsChatUnreachableError(
            "Bad Request: message text is empty");
        Assert.False(result);
    }

    [Fact]
    public void IsChatUnreachableError_returns_false_for_rate_limit_error()
    {
        var result = InvokeIsChatUnreachableError(
            "Too Many Requests: retry later");
        Assert.False(result);
    }

    // ─── Telegram message_id extraction (REQ-NOTIFY-023) ─────────────────────

    [Fact]
    public void ExtractMessageId_returns_message_id_from_valid_response()
    {
        var json = """{"ok":true,"result":{"message_id":12345,"chat":{"id":67890}}}""";
        var id = InvokeExtractMessageId(json);
        Assert.Equal("12345", id);
    }

    [Fact]
    public void ExtractMessageId_returns_null_when_ok_is_false()
    {
        var json = """{"ok":false,"error_code":400,"description":"Bad Request"}""";
        var id = InvokeExtractMessageId(json);
        Assert.Null(id);
    }

    [Fact]
    public void ExtractMessageId_returns_null_for_invalid_json()
    {
        var id = InvokeExtractMessageId("not json");
        Assert.Null(id);
    }

    [Fact]
    public void ExtractMessageId_returns_null_for_empty_response()
    {
        var id = InvokeExtractMessageId("");
        Assert.Null(id);
    }

    // ─── Helper methods (invoke private statics via reflection) ───────────────

    private static int InvokeParseRetryAfter(HttpResponseHeaders headers)
    {
        var method = typeof(NotificationDeliveryService).GetMethod(
            "ParseRetryAfter",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        return (int)method!.Invoke(null, [headers])!;
    }

    private static bool InvokeIsChatUnreachableError(string errorBody)
    {
        var method = typeof(NotificationDeliveryService).GetMethod(
            "IsChatUnreachableError",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        return (bool)method!.Invoke(null, [errorBody])!;
    }

    private static string? InvokeExtractMessageId(string responseBody)
    {
        var method = typeof(NotificationDeliveryService).GetMethod(
            "ExtractMessageId",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        return (string?)method!.Invoke(null, [responseBody]);
    }
}
