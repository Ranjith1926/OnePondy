using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Application.Media;
using OnePondy.Application.Places;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Places;

namespace OnePondy.Api.Http;

public sealed record UpdatePlaceBody(uint Version, PlaceInput Place);

public sealed record VersionBody(uint Version);

public sealed record VerifyPlaceBody(uint Version, string? Source);

public sealed record RejectPlaceBody(uint Version, string? Reason);

public sealed record MediaOrderBody(IReadOnlyList<Guid> MediaIds);

/// <summary>Place management for the web console (spec §10, Phase 3).</summary>
public static class AdminPlaceEndpoints
{
    public static IEndpointRouteBuilder MapAdminPlaceEndpoints(this IEndpointRouteBuilder app)
    {
        var places = app.MapGroup("/api/v1/admin/places").RequireAuthorization(StaffPolicies.ContentRead).WithTags("Console places");

        places.MapGet("/", List);
        places.MapGet("/counts", (PlaceAdminService service, CancellationToken ct) => service.CountsAsync(ct));
        places.MapGet("/categories", (PlaceAdminService service, CancellationToken ct) => service.CategoriesAsync(ct));
        places.MapGet("/{id:guid}", Get);
        places.MapGet("/{id:guid}/history", History);

        var write = places.MapGroup("/").RequireAuthorization(StaffPolicies.ContentWrite);
        write.MapPost("/", Create);
        write.MapPut("/{id:guid}", Update);
        write.MapPost("/{id:guid}/submit", Submit);
        write.MapPost("/{id:guid}/verify", Verify);
        write.MapPost("/{id:guid}/reject", Reject);
        write.MapPost("/{id:guid}/publish", Publish);
        write.MapPost("/{id:guid}/unpublish", Unpublish);
        write.MapPost("/{id:guid}/confirm-review", ConfirmReview);

        // Multipart uploads authenticate with a bearer token, not cookies, so antiforgery does not apply.
        write.MapPost("/{id:guid}/media", Upload)
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(12 * 1024 * 1024));
        write.MapPut("/{id:guid}/media/order", Reorder);
        write.MapPut("/{id:guid}/media/{mediaId:guid}", UpdateMedia);
        write.MapDelete("/{id:guid}/media/{mediaId:guid}", DeleteMedia);

        return app;
    }

    private static async Task<IResult> List(
        PlaceAdminService service,
        HttpContext httpContext,
        string? status,
        string? category,
        string? q,
        bool? published,
        bool? reviewDue,
        int? page,
        int? pageSize,
        CancellationToken cancellationToken)
    {
        VerificationStatus? parsedStatus = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!EndpointResults.TryParse<VerificationStatus>(status, out var value))
            {
                return ProblemResults.From(
                    AppError.Validation(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] }),
                    httpContext);
            }

            parsedStatus = value;
        }

        var query = new PlaceListQuery(parsedStatus, category, q, published, reviewDue ?? false, page, pageSize);
        return Results.Ok(await service.ListAsync(query, cancellationToken));
    }

    private static async Task<IResult> Get(Guid id, PlaceAdminService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetAsync(id, ct), httpContext);

    private static async Task<IResult> History(
        Guid id,
        int? page,
        int? pageSize,
        AuditQueryService audit,
        CancellationToken cancellationToken) =>
        Results.Ok(await audit.ListAsync(
            new AuditQuery(AuditEntityTypes.Place, id.ToString(), null, null, page, pageSize),
            cancellationToken));

    private static async Task<IResult> Create(
        PlaceInput body,
        ClaimsPrincipal user,
        PlaceAdminService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(body, user.GetUserId(), cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/v1/admin/places/{result.Value.Id}", result.Value)
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> Update(
        Guid id,
        UpdatePlaceBody body,
        ClaimsPrincipal user,
        PlaceAdminService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.UpdateAsync(id, body.Version, body.Place, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Submit(
        Guid id,
        VersionBody body,
        ClaimsPrincipal user,
        PlaceAdminService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.SubmitAsync(id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Verify(
        Guid id,
        VerifyPlaceBody body,
        ClaimsPrincipal user,
        PlaceAdminService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.VerifyAsync(id, body.Version, body.Source, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Reject(
        Guid id,
        RejectPlaceBody body,
        ClaimsPrincipal user,
        PlaceAdminService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.RejectAsync(id, body.Version, body.Reason, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Publish(
        Guid id,
        VersionBody body,
        ClaimsPrincipal user,
        PlaceAdminService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.PublishAsync(id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Unpublish(
        Guid id,
        VersionBody body,
        ClaimsPrincipal user,
        PlaceAdminService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.UnpublishAsync(id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> ConfirmReview(
        Guid id,
        VerifyPlaceBody body,
        ClaimsPrincipal user,
        PlaceAdminService service,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await service.ConfirmReviewAsync(id, body.Version, body.Source, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Upload(
        Guid id,
        IFormFile? file,
        [FromForm] string? altText,
        [FromForm] string? caption,
        [FromForm] string? licence,
        [FromForm] string? attribution,
        [FromForm] string? sourceUrl,
        ClaimsPrincipal user,
        MediaService media,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            return ProblemResults.From(
                AppError.Validation(new Dictionary<string, string[]> { ["file"] = ["Choose an image to upload."] }),
                httpContext);
        }

        if (file.Length > 10 * 1024 * 1024)
        {
            return ProblemResults.From(MediaErrors.TooLarge, httpContext);
        }

        if (!EndpointResults.TryParse<MediaLicence>(licence, out var parsedLicence))
        {
            return ProblemResults.From(
                AppError.Validation(new Dictionary<string, string[]> { ["licence"] = ["Choose a licence."] }),
                httpContext);
        }

        using var buffer = new MemoryStream((int)file.Length);
        await file.CopyToAsync(buffer, cancellationToken);
        var result = await media.UploadAsync(
            id,
            buffer.GetBuffer().AsMemory(0, (int)buffer.Length),
            new MediaDetailsInput(altText, caption, parsedLicence, attribution, sourceUrl),
            user.GetUserId(),
            cancellationToken);
        return result.IsSuccess
            ? Results.Created($"/api/v1/admin/places/{id}/media/{result.Value.Id}", result.Value)
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> UpdateMedia(
        Guid id,
        Guid mediaId,
        MediaDetailsInput body,
        ClaimsPrincipal user,
        MediaService media,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await media.UpdateAsync(id, mediaId, body, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Reorder(
        Guid id,
        MediaOrderBody body,
        ClaimsPrincipal user,
        MediaService media,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.Ok(await media.ReorderAsync(id, body.MediaIds ?? [], user.GetUserId(), ct), httpContext);

    private static async Task<IResult> DeleteMedia(
        Guid id,
        Guid mediaId,
        ClaimsPrincipal user,
        MediaService media,
        HttpContext httpContext,
        CancellationToken ct) =>
        EndpointResults.NoContent(await media.DeleteAsync(id, mediaId, user.GetUserId(), ct), httpContext);
}
