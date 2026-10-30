using System.Security.Claims;
using OnePondy.Application.Common;
using OnePondy.Application.Delivery;
using OnePondy.Application.Identity;
using OnePondy.Domain.Delivery;

namespace OnePondy.Api.Http;

public sealed record SaveDriverBody(string? FullName, VehicleType? VehicleType, string? VehicleNumber, string? LicenceNumber, uint? Version);

public sealed record DutyBody(bool OnDuty, uint Version);

public sealed record DriverVersionBody(uint Version);

public sealed record DriverReasonBody(uint Version, string? Reason);

/// <summary>
/// Delivery drivers (spec Phase 8, step 8.6): a signed-in account applies and starts and ends shifts; administrators review. A driver
/// is an ordinary account with an approved driver profile, so these routes need a sign-in and nothing more: what a person may do
/// depends on their profile's status, which the service checks.
/// </summary>
public static class DriverEndpoints
{
    public static IEndpointRouteBuilder MapDriverEndpoints(this IEndpointRouteBuilder app)
    {
        var driver = app.MapGroup("/api/v1/driver").RequireAuthorization().WithTags("Driver");
        driver.MapGet("/me", GetMine);
        driver.MapPut("/me", SaveMine);
        driver.MapPost("/duty", SetDuty);

        var admin = app.MapGroup("/api/v1/admin/drivers").RequireAuthorization(StaffPolicies.Administration).WithTags("Console drivers");
        admin.MapGet("/", AdminList);
        admin.MapGet("/{userId:guid}", AdminGet);
        admin.MapPost("/{userId:guid}/approve", Approve);
        admin.MapPost("/{userId:guid}/reject", Reject);
        admin.MapPost("/{userId:guid}/suspend", Suspend);
        admin.MapPost("/{userId:guid}/reinstate", Reinstate);
        return app;
    }

    private static async Task<IResult> GetMine(ClaimsPrincipal user, DriverService service, CancellationToken ct) =>
        Results.Ok(await service.GetMineAsync(user.GetUserId(), ct));

    private static async Task<IResult> SaveMine(SaveDriverBody body, ClaimsPrincipal user, DriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(
            await service.SaveMineAsync(user.GetUserId(), new DriverInput(body.FullName, body.VehicleType, body.VehicleNumber, body.LicenceNumber, body.Version), ct),
            httpContext);

    private static async Task<IResult> SetDuty(DutyBody body, ClaimsPrincipal user, DriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.SetDutyAsync(user.GetUserId(), body.OnDuty, body.Version, ct), httpContext);

    private static async Task<IResult> AdminList(string? status, int? page, int? pageSize, DriverService service, HttpContext httpContext, CancellationToken ct)
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

        return Results.Ok(await service.AdminListAsync(new DriverListQuery(parsed, page, pageSize), ct));
    }

    private static async Task<IResult> AdminGet(Guid userId, DriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.AdminGetAsync(userId, ct), httpContext);

    private static async Task<IResult> Approve(Guid userId, DriverVersionBody body, ClaimsPrincipal user, DriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ApproveAsync(userId, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Reject(Guid userId, DriverReasonBody body, ClaimsPrincipal user, DriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.RejectAsync(userId, body.Version, body.Reason, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Suspend(Guid userId, DriverReasonBody body, ClaimsPrincipal user, DriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.SuspendAsync(userId, body.Version, body.Reason, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Reinstate(Guid userId, DriverVersionBody body, ClaimsPrincipal user, DriverService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ReinstateAsync(userId, body.Version, user.GetUserId(), ct), httpContext);
}
