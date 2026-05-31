namespace SignalStack.Api.Admin;

/// <summary>
/// Generic admin error response with an optional detail message.
/// Used across incident, kill-switch, user, signal-type, chaos-exercise,
/// and penetration-test admin endpoints.
/// </summary>
public sealed record AdminErrorResponse(string Error, string? Message = null);

/// <summary>
/// Step-up required error with the sensitive category that triggered it.
/// Used by config, phase, chaos-exercise, and penetration-test endpoints
/// that gate mutations behind step-up re-authentication (REQ-SEC-011).
/// </summary>
public sealed record AdminStepUpRequiredResponse(string Error, string Category);

/// <summary>
/// Simple error response with no category or message detail.
/// Used by config and phase endpoints for validation errors.
/// </summary>
public sealed record AdminSimpleErrorResponse(string Error);

/// <summary>
/// Validation error including the list of allowed values for the invalid field.
/// Used by chaos-exercise and penetration-test endpoints for enum-style field validation.
/// The <c>Allowed</c> value serializes as-is (int[], string[], etc.) for correct JSON output.
/// </summary>
public sealed record AdminAllowedValuesError(string Error, object Allowed);
