using System.Security.Claims;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using OnePondy.Application.Identity;
using OnePondy.Infrastructure.Security;

namespace OnePondy.Infrastructure.Identity;

public sealed class JwtOptions
{
    public const string SectionName = "Auth:Jwt";

    public string Issuer { get; set; } = "onepondy-api";

    public string Audience { get; set; } = "onepondy-app";

    /// <summary>Base64-encoded HMAC key of at least 32 bytes, from user secrets or the host environment.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public SymmetricSecurityKey CreateSecurityKey() => new(KeyMaterial.Decode(SigningKey));
}

/// <summary>Issues short-lived HS256 access tokens carrying the user, their roles and the session id.</summary>
public sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider time) : IAccessTokenIssuer
{
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(Guid userId, IReadOnlyCollection<string> roles, Guid sessionId)
    {
        var jwt = options.Value;
        var now = time.GetUtcNow();
        var expires = now + jwt.AccessTokenLifetime;

        var claims = new List<Claim>
        {
            new(AuthClaimTypes.Subject, userId.ToString()),
            new(AuthClaimTypes.SessionId, sessionId.ToString()),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        claims.AddRange(roles.Select(role => new Claim(AuthClaimTypes.Role, role)));

        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = jwt.Issuer,
            Audience = jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expires.UtcDateTime,
            SigningCredentials = new SigningCredentials(jwt.CreateSecurityKey(), SecurityAlgorithms.HmacSha256),
        });

        return new AccessToken(token, expires);
    }
}
