using System.Security.Claims;
using OnePondy.Application.Notifications;

namespace OnePondy.Api.Http;

public sealed record RegisterDeviceBody(string? Token, string? Platform);

/// <summary>Registering the signed-in phone for push notifications (spec Phase 7, D7: Expo push service).</summary>
public static class DeviceEndpoints
{
    public static IEndpointRouteBuilder MapDeviceEndpoints(this IEndpointRouteBuilder app)
    {
        var devices = app.MapGroup("/api/v1/devices").RequireAuthorization().WithTags("Devices");
        devices.MapPost("/", Register);
        devices.MapDelete("/{id:guid}", Unregister);
        return app;
    }

    // Tied to the caller's own session: when it ends, this phone stops receiving notifications.
    private static async Task<IResult> Register(
        RegisterDeviceBody body,
        ClaimsPrincipal user,
        DeviceService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        EndpointResults.Ok(await service.RegisterAsync(user.GetUserId(), user.GetSessionId(), body.Token, body.Platform, cancellationToken), httpContext);

    private static async Task<IResult> Unregister(
        Guid id,
        ClaimsPrincipal user,
        DeviceService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        EndpointResults.NoContent(await service.UnregisterAsync(user.GetUserId(), id, cancellationToken), httpContext);
}
