using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OnePondy.Application.Businesses;
using OnePondy.Application.Common;
using OnePondy.Domain.Businesses;

namespace OnePondy.Api.Http;

public sealed record BusinessApplicationBody(
    string? Name,
    string? BusinessType,
    string? Description,
    string? PhoneNumber,
    string? Email,
    string? Address,
    double? Lat,
    double? Lng,
    string? Gstin,
    string? FssaiLicenseNumber);

public sealed record UpdateBusinessBody(uint Version, BusinessApplicationBody Business);

public sealed record BusinessVersionBody(uint Version);

public sealed record RejectBusinessBody(uint Version, string? Reason);

public sealed record SuspendBusinessBody(uint Version, string? Reason);

/// <summary>Partner onboarding: applying, editing and reviewing a business (spec §10, Phase 6).</summary>
public static class BusinessEndpoints
{
    public static IEndpointRouteBuilder MapBusinessEndpoints(this IEndpointRouteBuilder app)
    {
        var partner = app.MapGroup("/api/v1/partner/businesses").RequireAuthorization().WithTags("Partner businesses");
        partner.MapGet("/", ListMine);
        partner.MapPost("/", Apply);
        partner.MapGet("/{id:guid}", GetMine);
        partner.MapPut("/{id:guid}", Update);
        partner.MapPost("/{id:guid}/documents", UploadDocument)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(12 * 1024 * 1024));
        partner.MapDelete("/{id:guid}/documents/{documentId:guid}", RemoveDocument);
        partner.MapGet("/{id:guid}/documents/{documentId:guid}", DownloadMine);

        var admin = app.MapGroup("/api/v1/admin/businesses").RequireAuthorization(StaffPolicies.Administration).WithTags("Console businesses");
        admin.MapGet("/", AdminList);
        admin.MapGet("/{id:guid}", AdminGet);
        admin.MapGet("/{id:guid}/documents/{documentId:guid}", AdminDownload);
        admin.MapPost("/{id:guid}/approve", Approve);
        admin.MapPost("/{id:guid}/reject", Reject);
        admin.MapPost("/{id:guid}/suspend", Suspend);
        admin.MapPost("/{id:guid}/reinstate", Reinstate);

        return app;
    }

    private static async Task<IResult> ListMine(ClaimsPrincipal user, BusinessService service, CancellationToken ct) =>
        Results.Ok(await service.ListMineAsync(user.GetUserId(), ct));

    private static async Task<IResult> GetMine(Guid id, ClaimsPrincipal user, BusinessService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetMineAsync(id, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Apply(
        BusinessApplicationBody body,
        ClaimsPrincipal user,
        BusinessService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var input = ParseInput(body, httpContext, out var error);
        if (error is not null)
        {
            return error;
        }

        var result = await service.ApplyAsync(user.GetUserId(), input, cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/v1/partner/businesses/{result.Value.Id}", result.Value)
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdateBusinessBody body,
        ClaimsPrincipal user,
        BusinessService service,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var input = ParseInput(body.Business, httpContext, out var error);
        if (error is not null)
        {
            return error;
        }

        return EndpointResults.Ok(await service.UpdateAsync(id, body.Version, user.GetUserId(), input, ct), httpContext);
    }

    private static async Task<IResult> UploadDocument(
        Guid id,
        IFormFile? file,
        [FromForm] string? kind,
        ClaimsPrincipal user,
        BusinessService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return ProblemResults.From(
                AppError.Validation(new Dictionary<string, string[]> { ["file"] = ["Choose a file to upload."] }),
                httpContext);
        }

        if (file.Length > BusinessService.MaxDocumentBytes)
        {
            return ProblemResults.From(
                AppError.Validation(new Dictionary<string, string[]> { ["file"] = ["That file is too large."] }),
                httpContext);
        }

        if (!EndpointResults.TryParse<BusinessDocumentKind>(kind, out var parsedKind))
        {
            return ProblemResults.From(
                AppError.Validation(new Dictionary<string, string[]> { ["kind"] = ["Choose a document type."] }),
                httpContext);
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        var result = await service.AddDocumentAsync(
            id,
            user.GetUserId(),
            parsedKind,
            buffer.GetBuffer().AsMemory(0, (int)buffer.Length),
            file.ContentType,
            cancellationToken);
        return result.IsSuccess ? Results.Created($"/api/v1/partner/businesses/{id}", result.Value) : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> RemoveDocument(
        Guid id,
        Guid documentId,
        ClaimsPrincipal user,
        BusinessService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.NoContent(await service.RemoveDocumentAsync(id, documentId, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> DownloadMine(
        Guid id,
        Guid documentId,
        ClaimsPrincipal user,
        BusinessService service,
        HttpContext httpContext,
        CancellationToken ct)
    {
        var result = await service.GetDocumentAsync(id, documentId, user.GetUserId(), ct);
        return result.IsSuccess ? Results.Bytes(result.Value.Content, result.Value.ContentType) : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> AdminDownload(Guid id, Guid documentId, BusinessService service, HttpContext httpContext, CancellationToken ct)
    {
        var result = await service.GetDocumentAsync(id, documentId, requireOwnerUserId: null, ct);
        return result.IsSuccess ? Results.Bytes(result.Value.Content, result.Value.ContentType) : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> AdminList(string? status, int? page, int? pageSize, BusinessService service, HttpContext httpContext, CancellationToken ct)
    {
        BusinessApprovalStatus? parsedStatus = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!EndpointResults.TryParse<BusinessApprovalStatus>(status, out var value))
            {
                return ProblemResults.From(
                    AppError.Validation(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] }),
                    httpContext);
            }

            parsedStatus = value;
        }

        return Results.Ok(await service.AdminListAsync(new BusinessListQuery(parsedStatus, page, pageSize), ct));
    }

    private static async Task<IResult> AdminGet(Guid id, BusinessService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.AdminGetAsync(id, ct), httpContext);

    private static async Task<IResult> Approve(
        Guid id,
        BusinessVersionBody body,
        ClaimsPrincipal user,
        BusinessService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.ApproveAsync(id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Reject(
        Guid id,
        RejectBusinessBody body,
        ClaimsPrincipal user,
        BusinessService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.RejectAsync(id, body.Version, body.Reason, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Suspend(
        Guid id,
        SuspendBusinessBody body,
        ClaimsPrincipal user,
        BusinessService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.SuspendAsync(id, body.Version, body.Reason, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Reinstate(
        Guid id,
        BusinessVersionBody body,
        ClaimsPrincipal user,
        BusinessService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.ReinstateAsync(id, body.Version, user.GetUserId(), ct), httpContext);

    private static BusinessApplicationInput ParseInput(BusinessApplicationBody body, HttpContext httpContext, out IResult? error)
    {
        if (!EndpointResults.TryParse<BusinessType>(body.BusinessType, out var type))
        {
            error = ProblemResults.From(
                AppError.Validation(new Dictionary<string, string[]> { ["businessType"] = ["Choose a business type."] }),
                httpContext);
            return null!;
        }

        error = null;
        return new BusinessApplicationInput(
            body.Name,
            type,
            body.Description,
            body.PhoneNumber,
            body.Email,
            body.Address,
            body.Lat,
            body.Lng,
            body.Gstin,
            body.FssaiLicenseNumber);
    }
}
