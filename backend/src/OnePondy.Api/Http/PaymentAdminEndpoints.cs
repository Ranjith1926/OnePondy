using System.Security.Claims;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Application.Payments;
using OnePondy.Domain.Payments;

namespace OnePondy.Api.Http;

public sealed record ResolveReconciliationBody(string? Note);

/// <summary>
/// What the console needs to see of reconciliation (spec Phase 7): the differences found between our payment records
/// and the gateway's, and a way for an administrator to close one with a note of what was done.
/// </summary>
public static class PaymentAdminEndpoints
{
    public static IEndpointRouteBuilder MapPaymentAdminEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/admin/payments").RequireAuthorization(StaffPolicies.Administration).WithTags("Console payments");
        group.MapGet("/exceptions", ListExceptions);
        group.MapGet("/exceptions/open-count", async (ReconciliationExceptionService service, CancellationToken ct) =>
            Results.Ok(new { open = await service.OpenCountAsync(ct) }));
        group.MapPost("/exceptions/{id:guid}/resolve", Resolve);
        return app;
    }

    private static async Task<IResult> ListExceptions(
        string? status,
        string? kind,
        int? page,
        int? pageSize,
        ReconciliationExceptionService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        ReconciliationStatus? parsedStatus = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!EndpointResults.TryParse<ReconciliationStatus>(status, out var value))
            {
                return ProblemResults.From(
                    AppError.Validation(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] }),
                    httpContext);
            }

            parsedStatus = value;
        }

        ReconciliationKind? parsedKind = null;
        if (!string.IsNullOrEmpty(kind))
        {
            if (!EndpointResults.TryParse<ReconciliationKind>(kind, out var value))
            {
                return ProblemResults.From(
                    AppError.Validation(new Dictionary<string, string[]> { ["kind"] = ["Unknown kind."] }),
                    httpContext);
            }

            parsedKind = value;
        }

        return Results.Ok(await service.ListAsync(new ReconciliationQuery(parsedStatus, parsedKind, page, pageSize), cancellationToken));
    }

    private static async Task<IResult> Resolve(
        Guid id,
        ResolveReconciliationBody body,
        ClaimsPrincipal user,
        ReconciliationExceptionService service,
        HttpContext httpContext,
        CancellationToken cancellationToken) =>
        EndpointResults.Ok(await service.ResolveAsync(id, body.Note, user.GetUserId(), cancellationToken), httpContext);
}
