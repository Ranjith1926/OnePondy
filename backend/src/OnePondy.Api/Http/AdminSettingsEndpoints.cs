using System.Security.Claims;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Application.Configuration;
using OnePondy.Application.Staff;
using OnePondy.Domain.Configuration;

namespace OnePondy.Api.Http;

public sealed record ModuleStatusBody(string? Status);

public sealed record MinAppVersionBody(string? MinSupportedAppVersion);

public sealed record AddStaffBody(string? Email, string? Role);

public sealed record ChangeRoleBody(string? Role);

/// <summary>Configuration, staff and audit log in the web console. Changes are for administrators only.</summary>
public static class AdminSettingsEndpoints
{
    public static IEndpointRouteBuilder MapAdminSettingsEndpoints(this IEndpointRouteBuilder app)
    {
        var config = app.MapGroup("/api/v1/admin/config").WithTags("Console configuration");
        config.MapGet("/", (ConfigService service, CancellationToken ct) => service.GetAdminAsync(ct))
            .RequireAuthorization(StaffPolicies.ContentRead);
        config.MapPut("/modules/{key}", SetModule).RequireAuthorization(StaffPolicies.Administration);
        config.MapPut("/min-app-version", SetMinVersion).RequireAuthorization(StaffPolicies.Administration);

        var staff = app.MapGroup("/api/v1/admin/staff").RequireAuthorization(StaffPolicies.Administration).WithTags("Console staff");
        staff.MapGet("/", (StaffService service, CancellationToken ct) => service.ListAsync(ct));
        staff.MapPost("/", AddStaff);
        staff.MapPut("/{userId:guid}", ChangeRole);
        staff.MapDelete("/{userId:guid}", RemoveStaff);

        app.MapGet("/api/v1/admin/audit", ListAudit).RequireAuthorization(StaffPolicies.Administration).WithTags("Console audit");
        return app;
    }

    private static async Task<IResult> SetModule(
        string key,
        ModuleStatusBody body,
        ClaimsPrincipal user,
        ConfigService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (!EndpointResults.TryParse<ModuleStatus>(body.Status, out var status))
        {
            return ProblemResults.From(
                AppError.Validation(new Dictionary<string, string[]> { ["status"] = ["Choose a status."] }),
                httpContext);
        }

        return EndpointResults.Ok(await service.SetModuleStatusAsync(key, status, user.GetUserId(), cancellationToken), httpContext);
    }

    private static async Task<IResult> SetMinVersion(
        MinAppVersionBody body,
        ClaimsPrincipal user,
        ConfigService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.SetMinSupportedAppVersionAsync(body.MinSupportedAppVersion, user.GetUserId(), cancellationToken);
        return result.IsSuccess
            ? Results.Ok(new MinAppVersionBody(result.Value))
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> AddStaff(
        AddStaffBody body,
        ClaimsPrincipal user,
        StaffService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.AddAsync(body.Email, body.Role, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> ChangeRole(
        Guid userId,
        ChangeRoleBody body,
        ClaimsPrincipal user,
        StaffService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.ChangeRoleAsync(userId, body.Role, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> RemoveStaff(
        Guid userId,
        ClaimsPrincipal user,
        StaffService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.NoContent(await service.RemoveAsync(userId, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> ListAudit(
        string? entityType,
        string? entityId,
        string? action,
        Guid? actor,
        int? page,
        int? pageSize,
        AuditQueryService audit,
        CancellationToken cancellationToken) =>
        Results.Ok(await audit.ListAsync(new AuditQuery(entityType, entityId, action, actor, page, pageSize), cancellationToken));
}
