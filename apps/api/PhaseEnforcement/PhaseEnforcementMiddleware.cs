using SignalStack.Api.SysConfig;

namespace SignalStack.Api.PhaseEnforcement;

/// <summary>
/// Enforces Phase A legal constraints (REQ-LEGAL-002):
///
/// - Blocks billing/payment/invoicing endpoints with HTTP 403 when the
///   operating phase is "A", reserving those URL namespaces for Phase B/C.
/// - Phase B or C lifts all enforcement — the middleware passes through.
///
/// Must be registered after UseAuthentication() / UseAuthorization() if it
/// later needs the authenticated user identity for audit logging.
/// </summary>
public sealed class PhaseEnforcementMiddleware
{
    private static readonly PathString BillingPrefix = "/api/v1/billing";
    private static readonly PathString PaymentPrefix = "/api/v1/payment";
    private static readonly PathString InvoicePrefix = "/api/v1/invoice";

    private static readonly PathString[] RestrictedPrefixes =
        [BillingPrefix, PaymentPrefix, InvoicePrefix];

    private readonly RequestDelegate _next;
    private readonly ILogger<PhaseEnforcementMiddleware> _logger;

    public PhaseEnforcementMiddleware(
        RequestDelegate next,
        ILogger<PhaseEnforcementMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ISysConfigRepository configRepo)
    {
        var path = context.Request.Path;

        foreach (var prefix in RestrictedPrefixes)
        {
            if (!path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase))
                continue;

            var phase = await configRepo.GetCurrentPhaseAsync(context.RequestAborted);

            if (phase == "A")
            {
                _logger.LogInformation(
                    "Phase A enforcement: blocked {Method} {Path}",
                    context.Request.Method, path.Value);

                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                await context.Response.WriteAsJsonAsync(new
                {
                    error = "phase_a_restricted",
                    message = "Billing and payment services are not available during Phase A evaluation."
                });
                return;
            }
        }

        await _next(context);
    }
}
