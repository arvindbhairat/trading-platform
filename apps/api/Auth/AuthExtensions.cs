using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

namespace SignalStack.Api.Auth;

public static class AuthExtensions
{
    public static IServiceCollection AddSignalStackAuth(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddSingleton<JwtTokenService>();

        // Register authentication schemes.
        var authBuilder = services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(); // TokenValidationParameters are set below via PostConfigure.

        // Resolve JWT validation parameters from DI's IConfiguration.
        // Using .Configure<IConfiguration> ensures the options are built from the
        // DI-registered IConfiguration (which includes all test-factory overrides),
        // rather than from the builder.Configuration captured at DI registration time —
        // the two differ in WebApplicationFactory test scenarios.
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IConfiguration>((options, config) =>
            {
                var secret = config["Auth:Jwt:Secret"] ?? "";
                if (string.IsNullOrWhiteSpace(secret)) return;

                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = config["Auth:Jwt:Issuer"] ?? "signalstack-api",
                    ValidateAudience = true,
                    ValidAudience = config["Auth:Jwt:Audience"] ?? "signalstack-portal",
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(
                        Encoding.UTF8.GetBytes(secret)),
                    ClockSkew = TimeSpan.FromSeconds(30)
                };

                options.Events = new JwtBearerEvents
                {
                    OnChallenge = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("SignalStack.Api.Auth.JwtBearer");
                        logger.LogWarning(
                            "JWT challenge: Error={Error} ErrorDescription={ErrorDescription} AuthHeader={AuthHeader}",
                            ctx.Error,
                            ctx.ErrorDescription,
                            ctx.Request.Headers.Authorization.FirstOrDefault()?.Substring(0, Math.Min(80, ctx.Request.Headers.Authorization.FirstOrDefault()?.Length ?? 0)));
                        return Task.CompletedTask;
                    },
                    OnAuthenticationFailed = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("SignalStack.Api.Auth.JwtBearer");
                        logger.LogError(ctx.Exception,
                            "JWT authentication failed: {Message}",
                            ctx.Exception?.Message);
                        return Task.CompletedTask;
                    },
                    OnTokenValidated = ctx =>
                    {
                        var logger = ctx.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("SignalStack.Api.Auth.JwtBearer");
                        var sub = ctx.Principal?.FindFirst("sub")?.Value ?? "(none)";
                        var jti = ctx.Principal?.FindFirst("jti")?.Value ?? "(none)";
                        logger.LogInformation(
                            "JWT validated: sub={Sub} jti={Jti} identityAuth={IsAuth}",
                            sub, jti, ctx.Principal?.Identity?.IsAuthenticated);
                        return Task.CompletedTask;
                    },
                };
            });

        // Short-lived cookie scheme for OAuth temp sign-in between the callback and the
        // JWT issuance. The RemoteAuthenticationHandler internally calls SignInAsync after
        // exchanging the authorization code — JwtBearerHandler can't sign in, so we need a
        // dedicated cookie scheme as the SignInScheme.
        authBuilder.AddCookie("OAuthTemp", options =>
        {
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
            options.SlidingExpiration = false;
        });

        // OAuth providers are conditionally registered when credentials are configured.
        // REQ-AUTH-011: the login page always shows all three providers regardless of whether
        // their server-side credentials are present; the provider list endpoint returns all three
        // unconditionally. Missing credentials only affect the redirect path for that provider.
        var googleClientId = configuration["Auth:Google:ClientId"];
        var googleSecret = configuration["Auth:Google:ClientSecret"];
        if (!string.IsNullOrWhiteSpace(googleClientId)
            && !string.IsNullOrWhiteSpace(googleSecret))
        {
            authBuilder.AddGoogle(options =>
            {
                options.ClientId = googleClientId;
                options.ClientSecret = googleSecret;
                options.CallbackPath = "/api/v1/auth/callback/google";
                options.SignInScheme = "OAuthTemp";
                options.SaveTokens = false;
            });
        }

        var msClientId = configuration["Auth:Microsoft:ClientId"];
        var msSecret = configuration["Auth:Microsoft:ClientSecret"];
        if (!string.IsNullOrWhiteSpace(msClientId)
            && !string.IsNullOrWhiteSpace(msSecret))
        {
            authBuilder.AddMicrosoftAccount(options =>
            {
                options.ClientId = msClientId;
                options.ClientSecret = msSecret;
                options.CallbackPath = "/api/v1/auth/callback/microsoft";
                options.SignInScheme = "OAuthTemp";
                options.SaveTokens = false;
            });
        }

        var fbAppId = configuration["Auth:Facebook:AppId"];
        var fbSecret = configuration["Auth:Facebook:AppSecret"];
        if (!string.IsNullOrWhiteSpace(fbAppId) && !string.IsNullOrWhiteSpace(fbSecret))
        {
            authBuilder.AddFacebook(options =>
            {
                options.AppId = fbAppId;
                options.AppSecret = fbSecret;
                options.CallbackPath = "/api/v1/auth/callback/facebook";
                options.SignInScheme = "OAuthTemp";
                options.SaveTokens = false;
            });
        }

        services.AddAuthorization();

        // CSRF: double-submit cookie pattern as defence-in-depth per REQ-SEC-001 and
        // engineering-standards § CSRF model. The XSRF-TOKEN cookie is readable by JS
        // (not HttpOnly) so the portal can copy it into the X-XSRF-TOKEN request header.
        services.AddAntiforgery(options =>
        {
            options.HeaderName = "X-XSRF-TOKEN";
            options.Cookie.Name = "XSRF-TOKEN";
            options.Cookie.SameSite = SameSiteMode.None;
            options.Cookie.HttpOnly = false;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        return services;
    }
}
