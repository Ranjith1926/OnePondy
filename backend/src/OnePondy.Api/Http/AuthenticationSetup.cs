using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OnePondy.Application.Identity;
using OnePondy.Infrastructure.Identity;
using OnePondy.Infrastructure.Security;

namespace OnePondy.Api.Http;

public static class AuthenticationSetup
{
    public static IServiceCollection AddOnePondyAuthentication(this IServiceCollection services)
    {
        // Fail at startup, with setup guidance, when the signing or hashing key is missing.
        services.AddOptions<JwtOptions>().ValidateOnStart();
        services.AddOptions<SecretHashingOptions>().ValidateOnStart();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwtOptions) =>
            {
                var jwt = jwtOptions.Value;
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.CreateSecurityKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = AuthClaimTypes.Subject,
                    RoleClaimType = AuthClaimTypes.Role,
                };
                bearer.Events = new JwtBearerEvents
                {
                    // Sign-out and account deletion take effect immediately, not when the token expires.
                    OnTokenValidated = async context =>
                    {
                        var sessionClaim = context.Principal?.FindFirstValue(AuthClaimTypes.SessionId);
                        var auth = context.HttpContext.RequestServices.GetRequiredService<AuthService>();
                        if (!Guid.TryParse(sessionClaim, out var sessionId)
                            || !await auth.IsSessionActiveAsync(sessionId, context.HttpContext.RequestAborted))
                        {
                            context.Fail("The session is no longer active.");
                        }
                    },
                };
            });

        services.AddAuthorization();
        return services;
    }
}
