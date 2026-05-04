namespace SignalStack.Api.Execution;

/// <summary>
/// Request to create a signed order payload for the FYERS API Connect widget.
/// P7-T5 / REQ-ORDER-009b.
/// </summary>
public sealed record SignedPayloadRequest(
    string Symbol,
    string Action,    // "entry" | "add" | "reduce" | "exit"
    int Quantity,
    string OrderType, // "MARKET" | "LIMIT"
    decimal? LimitPrice
);

/// <summary>
/// Signed payload response containing the nonce and data-* attributes
/// for the &lt;fyers-button&gt; element. P7-T5 / REQ-ORDER-009b.
/// </summary>
public sealed record SignedPayloadResponse(
    string Nonce,
    IReadOnlyDictionary<string, string> DataAttributes,
    string PayloadHash,
    long ExpiresAtUnix
);

/// <summary>
/// Error detail returned when the signed-payload endpoint refuses to sign.
/// Carries a machine-readable reason and a human-readable message.
/// </summary>
public sealed record SignedPayloadError(
    string Reason,
    string? Message,
    DateTime? LastSuccessfulSyncAt = null
);
