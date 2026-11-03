using System.Security.Claims;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Application.Rides;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Rides;

namespace OnePondy.Api.Http;

public sealed record SaveRideDriverBody(
    string? FullName,
    RideVehicleClass? VehicleClass,
    string? VehicleNumber,
    string? LicenceNumber,
    string? PermitNumber,
    string? InsurancePolicyNumber,
    DateOnly? InsuranceValidUntil,
    uint? Version);

public sealed record RideQuoteBody(
    double? PickupLatitude,
    double? PickupLongitude,
    string? PickupLabel,
    double? DestinationLatitude,
    double? DestinationLongitude,
    string? DestinationLabel);

public sealed record RequestRideBody(Guid? QuoteId, string? PickupNote);

public sealed record RideReasonBody(string? Reason);

public sealed record SosBody(double? Latitude, double? Longitude);

public sealed record IncidentBody(RideSafetyCategory? Category, string? Details);

public sealed record ResolveSafetyBody(uint Version, string? Note);

public sealed record SafetyVersionBody(uint Version);

/// <summary>
/// Rides (spec Phase 9). Riders ask for fares, request and follow a ride and cancel it; drivers in ride mode apply, go on duty, share their position,
/// take rides and move them along; administrators review drivers. Every route needs a sign-in and what a person may do depends on their own profile
/// and ride, which the services check. Asking for a fare or a ride also needs the <c>rides</c> module, which stays off until the owner is ready.
/// </summary>
public static class RideEndpoints
{
    public static IEndpointRouteBuilder MapRideEndpoints(this IEndpointRouteBuilder app)
    {
        var rides = app.MapGroup("/api/v1/rides").RequireAuthorization().WithTags("Rides");
        rides.MapPost("/quotes", Quote);
        rides.MapPost("/", Request);
        rides.MapGet("/current", Current);
        rides.MapGet("/", ListMine);
        rides.MapGet("/{rideId:guid}", GetOne);
        rides.MapPost("/{rideId:guid}/cancel", Cancel);
        rides.MapPost("/{rideId:guid}/sos", RiderSos);
        rides.MapPost("/{rideId:guid}/incidents", RiderIncident);
        rides.MapPost("/{rideId:guid}/share", CreateShare);
        rides.MapDelete("/{rideId:guid}/share", RevokeShare);

        // Someone following a shared trip has no account: the secret in the link is the whole credential.
        var shared = app.MapGroup("/api/v1/shared-rides").AllowAnonymous().WithTags("Shared rides");
        shared.MapGet("/{token}", GetShared);

        var driver = app.MapGroup("/api/v1/ride-driver").RequireAuthorization().WithTags("Ride driver");
        driver.MapGet("/me", GetMine);
        driver.MapPut("/me", SaveMine);
        driver.MapPost("/duty", SetDuty);
        driver.MapPost("/location", ShareLocation);
        driver.MapGet("/rides", DriverRides);
        driver.MapPost("/rides/{rideId:guid}/accept", Accept);
        driver.MapPost("/rides/{rideId:guid}/give-up", GiveUp);
        driver.MapPost("/rides/{rideId:guid}/arrive", Arrive);
        driver.MapPost("/rides/{rideId:guid}/start", Start);
        driver.MapPost("/rides/{rideId:guid}/complete", Complete);
        driver.MapPost("/rides/{rideId:guid}/cancel", DriverCancel);
        driver.MapPost("/rides/{rideId:guid}/location", ShareRideLocation);
        driver.MapPost("/rides/{rideId:guid}/sos", DriverSos);
        driver.MapPost("/rides/{rideId:guid}/incidents", DriverIncident);

        var safety = app.MapGroup("/api/v1/admin/ride-safety").RequireAuthorization(StaffPolicies.Support).WithTags("Console ride safety");
        safety.MapGet("/", SafetyList);
        safety.MapGet("/counts", SafetyCounts);
        safety.MapGet("/{id:guid}", SafetyGet);
        safety.MapPost("/{id:guid}/acknowledge", SafetyAcknowledge);
        safety.MapPost("/{id:guid}/resolve", SafetyResolve);

        var admin = app.MapGroup("/api/v1/admin/ride-drivers").RequireAuthorization(StaffPolicies.Administration).WithTags("Console ride drivers");
        admin.MapGet("/", AdminList);
        admin.MapGet("/{userId:guid}", AdminGet);
        admin.MapPost("/{userId:guid}/approve", Approve);
        admin.MapPost("/{userId:guid}/reject", Reject);
        admin.MapPost("/{userId:guid}/suspend", Suspend);
        admin.MapPost("/{userId:guid}/reinstate", Reinstate);
        return app;
    }

    // --- The rider ---

    private static async Task<IResult> Quote(RideQuoteBody body, ClaimsPrincipal user, RideService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(
            await service.QuoteAsync(
                user.GetUserId(),
                new RideQuoteInput(body.PickupLatitude, body.PickupLongitude, body.PickupLabel, body.DestinationLatitude, body.DestinationLongitude, body.DestinationLabel),
                ct),
            httpContext);

    private static async Task<IResult> Request(RequestRideBody body, ClaimsPrincipal user, RideService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.RequestAsync(user.GetUserId(), new RequestRideInput(body.QuoteId, body.PickupNote), ct), httpContext);

    private static async Task<IResult> Current(ClaimsPrincipal user, RideService service, CancellationToken ct) =>
        await service.GetCurrentAsync(user.GetUserId(), ct) is { } ride ? Results.Ok(ride) : Results.NoContent();

    private static async Task<IResult> ListMine(int? page, int? pageSize, ClaimsPrincipal user, RideService service, CancellationToken ct) =>
        Results.Ok(await service.ListMineAsync(user.GetUserId(), page, pageSize, ct));

    private static async Task<IResult> GetOne(Guid rideId, ClaimsPrincipal user, RideService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetAsync(user.GetUserId(), rideId, ct), httpContext);

    private static async Task<IResult> Cancel(Guid rideId, RideReasonBody? body, ClaimsPrincipal user, RideService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.CancelAsync(user.GetUserId(), rideId, body?.Reason, ct), httpContext);

    private static async Task<IResult> RiderSos(Guid rideId, SosBody? body, ClaimsPrincipal user, RideSafetyService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.RaiseSosAsync(user.GetUserId(), RideParty.Rider, rideId, new RaiseSosInput(body?.Latitude, body?.Longitude), ct), httpContext);

    private static async Task<IResult> RiderIncident(Guid rideId, IncidentBody body, ClaimsPrincipal user, RideSafetyService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ReportIncidentAsync(user.GetUserId(), RideParty.Rider, rideId, new ReportIncidentInput(body.Category, body.Details), ct), httpContext);

    private static async Task<IResult> CreateShare(Guid rideId, ClaimsPrincipal user, RideSafetyService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.CreateShareAsync(user.GetUserId(), rideId, ct), httpContext);

    private static async Task<IResult> RevokeShare(Guid rideId, ClaimsPrincipal user, RideSafetyService service, HttpContext httpContext, CancellationToken ct)
    {
        var result = await service.RevokeShareAsync(user.GetUserId(), rideId, ct);
        return result.IsSuccess ? Results.NoContent() : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> GetShared(string token, RideSafetyService service, HttpContext httpContext, CancellationToken ct)
    {
        httpContext.Response.Headers.CacheControl = "no-store";
        return await service.GetSharedAsync(token, ct) is { } view ? Results.Ok(view) : Results.NotFound();
    }

    // --- The driver ---

    private static async Task<IResult> DriverSos(Guid rideId, SosBody? body, ClaimsPrincipal user, RideSafetyService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.RaiseSosAsync(user.GetUserId(), RideParty.Driver, rideId, new RaiseSosInput(body?.Latitude, body?.Longitude), ct), httpContext);

    private static async Task<IResult> DriverIncident(Guid rideId, IncidentBody body, ClaimsPrincipal user, RideSafetyService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ReportIncidentAsync(user.GetUserId(), RideParty.Driver, rideId, new ReportIncidentInput(body.Category, body.Details), ct), httpContext);

    private static async Task<IResult> GetMine(ClaimsPrincipal user, RideDriverService service, CancellationToken ct) =>
        Results.Ok(await service.GetMineAsync(user.GetUserId(), ct));

    private static async Task<IResult> SaveMine(SaveRideDriverBody body, ClaimsPrincipal user, RideDriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(
            await service.SaveMineAsync(
                user.GetUserId(),
                new RideDriverInput(
                    body.FullName, body.VehicleClass, body.VehicleNumber, body.LicenceNumber, body.PermitNumber, body.InsurancePolicyNumber, body.InsuranceValidUntil, body.Version),
                ct),
            httpContext);

    private static async Task<IResult> SetDuty(DutyBody body, ClaimsPrincipal user, RideDriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.SetDutyAsync(user.GetUserId(), body.OnDuty, body.Version, ct), httpContext);

    private static async Task<IResult> ShareLocation(LocationBody body, ClaimsPrincipal user, RideDriverService service, HttpContext httpContext, CancellationToken ct)
    {
        var result = await service.ShareLocationAsync(user.GetUserId(), body.Latitude, body.Longitude, ct);
        return result.IsSuccess ? Results.NoContent() : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> DriverRides(ClaimsPrincipal user, RideService service, CancellationToken ct) =>
        Results.Ok(await service.GetMineAsync(user.GetUserId(), ct));

    private static async Task<IResult> Accept(Guid rideId, ClaimsPrincipal user, RideService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.AcceptAsync(user.GetUserId(), rideId, ct), httpContext);

    private static async Task<IResult> GiveUp(Guid rideId, ClaimsPrincipal user, RideService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GiveUpAsync(user.GetUserId(), rideId, ct), httpContext);

    private static async Task<IResult> Arrive(Guid rideId, ClaimsPrincipal user, RideService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ArriveAsync(user.GetUserId(), rideId, ct), httpContext);

    private static async Task<IResult> Start(Guid rideId, ClaimsPrincipal user, RideService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.StartAsync(user.GetUserId(), rideId, ct), httpContext);

    private static async Task<IResult> Complete(Guid rideId, DeliverBody? body, ClaimsPrincipal user, RideService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.CompleteAsync(user.GetUserId(), rideId, body?.CashCollected, ct), httpContext);

    private static async Task<IResult> DriverCancel(Guid rideId, RideReasonBody? body, ClaimsPrincipal user, RideService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.CancelAsDriverAsync(user.GetUserId(), rideId, body?.Reason, ct), httpContext);

    private static async Task<IResult> ShareRideLocation(Guid rideId, LocationBody body, ClaimsPrincipal user, RideService service, HttpContext httpContext, CancellationToken ct)
    {
        var result = await service.RecordLocationAsync(user.GetUserId(), rideId, body.Latitude, body.Longitude, ct);
        return result.IsSuccess ? Results.NoContent() : ProblemResults.From(result.Error, httpContext);
    }

    // --- Staff: the safety queue ---

    private static async Task<IResult> SafetyList(string? view, int? page, int? pageSize, RideSafetyService service, CancellationToken ct) =>
        Results.Ok(await service.ListAsync(new SafetyListQuery(view, page, pageSize), ct));

    private static async Task<IResult> SafetyCounts(RideSafetyService service, CancellationToken ct) =>
        Results.Ok(await service.CountsAsync(ct));

    private static async Task<IResult> SafetyGet(Guid id, ClaimsPrincipal user, RideSafetyService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetAsync(id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> SafetyAcknowledge(Guid id, SafetyVersionBody body, ClaimsPrincipal user, RideSafetyService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.AcknowledgeAsync(id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> SafetyResolve(Guid id, ResolveSafetyBody body, ClaimsPrincipal user, RideSafetyService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ResolveAsync(id, new ResolveSafetyInput(body.Version, body.Note), user.GetUserId(), ct), httpContext);

    // --- Administrators ---

    private static async Task<IResult> AdminList(string? status, int? page, int? pageSize, RideDriverService service, HttpContext httpContext, CancellationToken ct)
    {
        DriverApprovalStatus? parsed = null;
        if (!string.IsNullOrWhiteSpace(status))
        {
            if (!EndpointResults.TryParse<DriverApprovalStatus>(status, out var value))
            {
                return ProblemResults.From(
                    AppError.Validation(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] }),
                    httpContext);
            }

            parsed = value;
        }

        return Results.Ok(await service.AdminListAsync(new RideDriverListQuery(parsed, page, pageSize), ct));
    }

    private static async Task<IResult> AdminGet(Guid userId, RideDriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.AdminGetAsync(userId, ct), httpContext);

    private static async Task<IResult> Approve(Guid userId, DriverVersionBody body, ClaimsPrincipal user, RideDriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ApproveAsync(userId, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Reject(Guid userId, DriverReasonBody body, ClaimsPrincipal user, RideDriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.RejectAsync(userId, body.Version, body.Reason, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Suspend(Guid userId, DriverReasonBody body, ClaimsPrincipal user, RideDriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.SuspendAsync(userId, body.Version, body.Reason, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Reinstate(Guid userId, DriverVersionBody body, ClaimsPrincipal user, RideDriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ReinstateAsync(userId, body.Version, user.GetUserId(), ct), httpContext);
}
