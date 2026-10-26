using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;
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

        // Console access by staff role (spec §10, Phase 3). The API decides; the console only hides links.
        services.AddAuthorizationBuilder()
            .AddPolicy(StaffPolicies.ContentRead, policy => policy.RequireRole(
                RoleNames.Administrator,
                RoleNames.ContentEditor,
                RoleNames.SupportAgent))
            .AddPolicy(StaffPolicies.ContentWrite, policy => policy.RequireRole(RoleNames.Administrator, RoleNames.ContentEditor))
            .AddPolicy(StaffPolicies.Administration, policy => policy.RequireRole(RoleNames.Administrator));
        return services;
    }
}

public static class StaffPolicies
{
    /// <summary>Any staff member: view places, configuration and history.</summary>
    public const string ContentRead = "content:read";

    /// <summary>Administrators and content editors: edit, verify and publish places and photos.</summary>
    public const string ContentWrite = "content:write";

    /// <summary>Administrators only: configuration, staff and the audit log.</summary>
    public const string Administration = "administration";
}
