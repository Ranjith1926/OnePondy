using System.Security.Claims;
using System.Text;
using OnePondy.Application.Common;
using OnePondy.Application.Identity;
using OnePondy.Application.Payments;
using OnePondy.Domain.Payments;

namespace OnePondy.Api.Http;

public sealed record DraftSettlementBody(Guid BusinessId, DateOnly PeriodStart, DateOnly PeriodEnd);

public sealed record SettlementVersionBody(uint Version);

public sealed record MarkSettlementPaidBody(uint Version, string? PayoutReference);

/// <summary>
/// Payments, refunds and partner statements (spec Phase 7). Administrators see money in and out and take a statement through
/// draft, approved and paid; a partner sees only their own approved and paid statements, and can download them as CSV.
/// </summary>
public static class SettlementEndpoints
{
    public static IEndpointRouteBuilder MapSettlementEndpoints(this IEndpointRouteBuilder app)
    {
        var admin = app.MapGroup("/api/v1/admin").RequireAuthorization(StaffPolicies.Administration).WithTags("Console payments");
        admin.MapGet("/payments", ListPayments);
        admin.MapGet("/refunds", ListRefunds);
        admin.MapGet("/settlements", ListSettlements);
        admin.MapGet("/settlements/{id:guid}", GetSettlement);
        admin.MapPost("/settlements", Draft);
        admin.MapPost("/settlements/{id:guid}/approve", Approve);
        admin.MapPost("/settlements/{id:guid}/paid", MarkPaid);
        admin.MapPost("/settlements/{id:guid}/discard", Discard);

        var partner = app.MapGroup("/api/v1/partner/businesses/{businessId:guid}/statements").RequireAuthorization().WithTags("Partner statements");
        partner.MapGet("/", ListStatements);
        partner.MapGet("/{id:guid}", GetStatement);
        partner.MapGet("/{id:guid}/csv", DownloadStatement);
        return app;
    }

    private static IResult? BadFilter(HttpContext httpContext, string field, string noun) =>
        ProblemResults.From(AppError.Validation(new Dictionary<string, string[]> { [field] = [$"Unknown {noun}."] }), httpContext);

    private static async Task<IResult> ListPayments(
        string? status, Guid? businessId, int? page, int? pageSize, PaymentAdminService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        PaymentAttemptStatus? parsed = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!EndpointResults.TryParse<PaymentAttemptStatus>(status, out var value))
            {
                return BadFilter(httpContext, "status", "status")!;
            }

            parsed = value;
        }

        return Results.Ok(await service.ListPaymentsAsync(new PaymentQuery(parsed, businessId, page, pageSize), cancellationToken));
    }

    private static async Task<IResult> ListRefunds(
        string? status, int? page, int? pageSize, PaymentAdminService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        RefundStatus? parsed = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!EndpointResults.TryParse<RefundStatus>(status, out var value))
            {
                return BadFilter(httpContext, "status", "status")!;
            }

            parsed = value;
        }

        return Results.Ok(await service.ListRefundsAsync(new RefundQuery(parsed, page, pageSize), cancellationToken));
    }

    private static async Task<IResult> ListSettlements(
        string? status, Guid? businessId, int? page, int? pageSize, SettlementService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        SettlementStatus? parsed = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!EndpointResults.TryParse<SettlementStatus>(status, out var value))
            {
                return BadFilter(httpContext, "status", "status")!;
            }

            parsed = value;
        }

        return Results.Ok(await service.ListAsync(new SettlementQuery(parsed, businessId, page, pageSize), cancellationToken));
    }

    private static async Task<IResult> GetSettlement(Guid id, SettlementService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        EndpointResults.Ok(await service.GetAsync(id, cancellationToken), httpContext);

    private static async Task<IResult> Draft(
        DraftSettlementBody body, ClaimsPrincipal user, SettlementService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await service.DraftAsync(body.BusinessId, body.PeriodStart, body.PeriodEnd, user.GetUserId(), cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/v1/admin/settlements/{result.Value.Settlement.Id}", result.Value)
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> Approve(
        Guid id, SettlementVersionBody body, ClaimsPrincipal user, SettlementService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        EndpointResults.Ok(await service.ApproveAsync(id, body.Version, user.GetUserId(), cancellationToken), httpContext);

    private static async Task<IResult> MarkPaid(
        Guid id, MarkSettlementPaidBody body, ClaimsPrincipal user, SettlementService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        EndpointResults.Ok(await service.MarkPaidAsync(id, body.Version, body.PayoutReference, user.GetUserId(), cancellationToken), httpContext);

    private static async Task<IResult> Discard(
        Guid id, SettlementVersionBody body, ClaimsPrincipal user, SettlementService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        EndpointResults.NoContent(await service.DiscardDraftAsync(id, body.Version, user.GetUserId(), cancellationToken), httpContext);

    private static async Task<IResult> ListStatements(
        Guid businessId, ClaimsPrincipal user, SettlementService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        EndpointResults.Ok(await service.ListForBusinessAsync(businessId, user.GetUserId(), cancellationToken), httpContext);

    private static async Task<IResult> GetStatement(
        Guid businessId, Guid id, ClaimsPrincipal user, SettlementService service, HttpContext httpContext, CancellationToken cancellationToken) =>
        EndpointResults.Ok(await service.GetForBusinessAsync(businessId, id, user.GetUserId(), cancellationToken), httpContext);

    private static async Task<IResult> DownloadStatement(
        Guid businessId, Guid id, ClaimsPrincipal user, SettlementService service, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await service.CsvForBusinessAsync(businessId, id, user.GetUserId(), cancellationToken);
        if (!result.IsSuccess)
        {
            return ProblemResults.From(result.Error, httpContext);
        }

        // A byte-order mark so Excel reads the rupee sign and any non-English names correctly.
        var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true).GetPreamble().Concat(Encoding.UTF8.GetBytes(result.Value.Content)).ToArray();
        return Results.File(bytes, "text/csv; charset=utf-8", result.Value.FileName);
    }
}
