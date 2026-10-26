using OnePondy.Application.Identity;
using OnePondy.Domain.Identity;

namespace OnePondy.Api.Http;

public sealed record OtpRequestBody(OtpChannel Channel, string? Destination);

public sealed record OtpRequestedResponse(
    Guid ChallengeId,
    DateTimeOffset ExpiresAt,
    int ResendAfterSeconds,
    string MaskedDestination)
{
    public static OtpRequestedResponse From(OtpRequested requested) => new(
        requested.ChallengeId,
        requested.ExpiresAt,
        (int)Math.Ceiling(requested.ResendAfter.TotalSeconds),
        requested.MaskedDestination);
}

public sealed record VerifyCodeBody(Guid ChallengeId, string? Code, string? DeviceName);

public sealed record RefreshBody(string? RefreshToken);

public sealed record SessionUserResponse(Guid Id, string DisplayName, IReadOnlyList<string> Roles);

public sealed record SessionResponse(
    string AccessToken,
    DateTimeOffset AccessTokenExpiresAt,
    string RefreshToken,
    DateTimeOffset RefreshTokenExpiresAt,
    SessionUserResponse User,
    bool IsNewUser)
{
    public static SessionResponse From(SessionTokens tokens) => new(
        tokens.AccessToken.Value,
        tokens.AccessToken.ExpiresAt,
        tokens.RefreshToken,
        tokens.RefreshTokenExpiresAt,
        new SessionUserResponse(tokens.User.Id, tokens.User.DisplayName, tokens.User.Roles),
        tokens.User.IsNew);
}

public sealed record ProfileResponse(
    Guid Id,
    string DisplayName,
    string? PhoneNumber,
    string? Email,
    string PreferredLanguage,
    DateTimeOffset CreatedAt)
{
    public static ProfileResponse From(AccountProfile profile) => new(
        profile.Id,
        profile.DisplayName,
        profile.PhoneNumber,
        profile.Email,
        profile.PreferredLanguage,
        profile.CreatedAt);
}

public sealed record UpdateProfileBody(string? DisplayName, string? PreferredLanguage);

public sealed record ConfirmCodeBody(Guid ChallengeId, string? Code);

public sealed record DeletionRequestStatus(string Status);
