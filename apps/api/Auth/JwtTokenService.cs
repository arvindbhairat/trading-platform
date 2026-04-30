using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace SignalStack.Api.Auth;

public sealed class JwtTokenService
{
    private readonly SymmetricSecurityKey _key;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _expiryMinutes;
    private readonly JsonWebTokenHandler _handler = new();

    public JwtTokenService(IConfiguration configuration)
    {
        var secret = configuration["Auth:Jwt:Secret"]
            ?? throw new InvalidOperationException("Auth:Jwt:Secret is required.");
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        _issuer = configuration["Auth:Jwt:Issuer"] ?? "signalstack-api";
        _audience = configuration["Auth:Jwt:Audience"] ?? "signalstack-portal";
        _expiryMinutes = int.TryParse(configuration["Auth:Jwt:ExpiryMinutes"], out var m) ? m : 1440;
    }

    public string IssueToken(string userId, string email, string name, string provider)
        => IssueTokenWithMeta(userId, email, name, provider).Jwt;

    /// <summary>
    /// Issues a signed JWT and returns it alongside the claims needed to create the
    /// server-side session record (REQ-SESSION-002a).
    /// </summary>
    public (string Jwt, string Jti, DateTime IssuedAt, DateTime ExpiresAt) IssueTokenWithMeta(
        string userId, string email, string name, string provider)
    {
        var jti = Guid.NewGuid().ToString();
        var issuedAt = DateTime.UtcNow;
        var expiresAt = issuedAt.AddMinutes(_expiryMinutes);

        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId),
                new Claim(JwtRegisteredClaimNames.Email, email),
                new Claim(JwtRegisteredClaimNames.Name, name),
                new Claim("provider", provider),
                new Claim(JwtRegisteredClaimNames.Jti, jti)
            ]),
            Issuer = _issuer,
            Audience = _audience,
            NotBefore = issuedAt,
            Expires = expiresAt,
            SigningCredentials = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256)
        };

        var jwt = _handler.CreateToken(descriptor);
        return (jwt, jti, issuedAt, expiresAt);
    }
}
