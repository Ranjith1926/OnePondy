using System.Security.Claims;
using OnePondy.Application.Common;
using OnePondy.Application.Reviews;
using OnePondy.Domain.Reviews;

namespace OnePondy.Api.Http;

public sealed record CreateReviewBody(Guid? BookingId, Guid? FoodOrderId, int? Rating, string? Text);

public sealed record EditReviewBody(uint Version, int? Rating, string? Text);

public sealed record ReplyReviewBody(uint Version, string? Text);

public sealed record ReviewVersionBody(uint Version);

public sealed record ReviewReasonBody(uint Version, string? Reason);

/// <summary>
/// Reviews of completed bookings and delivered food orders (spec §7 and Phase 10). Customers write and manage their own, anyone reads the
/// published ones, a business owner answers them, and moderators (administrators and content editors) decide what is published.
/// </summary>
public static class ReviewEndpoints
{
    public static IEndpointRouteBuilder MapReviewEndpoints(this IEndpointRouteBuilder app)
    {
        var reviews = app.MapGroup("/api/v1/reviews").WithTags("Reviews");
        reviews.MapGet("/", ListPublic);
        reviews.MapPost("/", Create).RequireAuthorization();
        reviews.MapGet("/mine", ListMine).RequireAuthorization();
        reviews.MapPut("/{id:guid}", Edit).RequireAuthorization();
        reviews.MapDelete("/{id:guid}", Delete).RequireAuthorization();

        var partner = app.MapGroup("/api/v1/partner/businesses/{businessId:guid}/reviews").RequireAuthorization().WithTags("Partner reviews");
        partner.MapGet("/", ListForBusiness);
        partner.MapPost("/{id:guid}/reply", Reply);

        var admin = app.MapGroup("/api/v1/admin/reviews").RequireAuthorization(StaffPolicies.ContentWrite).WithTags("Console reviews");
        admin.MapGet("/", AdminList);
        admin.MapPost("/{id:guid}/publish", Publish);
        admin.MapPost("/{id:guid}/reject", Reject);
        admin.MapPost("/{id:guid}/remove", Remove);

        return app;
    }

    private static async Task<IResult> ListPublic(
        Guid? listingId, Guid? restaurantId, int? page, int? pageSize, ReviewService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ListPublicAsync(listingId, restaurantId, page, pageSize, ct), httpContext);

    private static async Task<IResult> Create(CreateReviewBody body, ClaimsPrincipal user, ReviewService service, HttpContext httpContext, CancellationToken ct)
    {
        var result = await service.CreateAsync(user.GetUserId(), new ReviewInput(body.BookingId, body.FoodOrderId, body.Rating, body.Text), ct);
        return result.IsSuccess
            ? Results.Created($"/api/v1/reviews/{result.Value.Id}", result.Value)
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> ListMine(ClaimsPrincipal user, ReviewService service, CancellationToken ct) =>
        Results.Ok(await service.ListMineAsync(user.GetUserId(), ct));

    private static async Task<IResult> Edit(Guid id, EditReviewBody body, ClaimsPrincipal user, ReviewService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.EditAsync(user.GetUserId(), id, new ReviewEditInput(body.Version, body.Rating, body.Text), ct), httpContext);

    private static async Task<IResult> Delete(Guid id, ClaimsPrincipal user, ReviewService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.NoContent(await service.DeleteAsync(user.GetUserId(), id, ct), httpContext);

    private static async Task<IResult> ListForBusiness(
        Guid businessId, int? page, int? pageSize, ClaimsPrincipal user, ReviewService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ListForBusinessAsync(businessId, user.GetUserId(), page, pageSize, ct), httpContext);

    private static async Task<IResult> Reply(
        Guid businessId, Guid id, ReplyReviewBody body, ClaimsPrincipal user, ReviewService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ReplyAsync(businessId, id, user.GetUserId(), new ReplyInput(body.Version, body.Text), ct), httpContext);

    private static async Task<IResult> AdminList(string? status, int? page, int? pageSize, ReviewService service, HttpContext httpContext, CancellationToken ct)
    {
        ReviewStatus? parsed = null;
        if (!string.IsNullOrEmpty(status))
        {
            if (!EndpointResults.TryParse<ReviewStatus>(status, out var value))
            {
                return ProblemResults.From(
                    AppError.Validation(new Dictionary<string, string[]> { ["status"] = ["Unknown status."] }),
                    httpContext);
            }

            parsed = value;
        }

        return Results.Ok(await service.AdminListAsync(new AdminReviewQuery(parsed, page, pageSize), ct));
    }

    private static async Task<IResult> Publish(Guid id, ReviewVersionBody body, ClaimsPrincipal user, ReviewService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.PublishAsync(id, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Reject(Guid id, ReviewReasonBody body, ClaimsPrincipal user, ReviewService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.RejectAsync(id, new ModerationInput(body.Version, body.Reason), user.GetUserId(), ct), httpContext);

    private static async Task<IResult> Remove(Guid id, ReviewReasonBody body, ClaimsPrincipal user, ReviewService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.RemoveAsync(id, new ModerationInput(body.Version, body.Reason), user.GetUserId(), ct), httpContext);
}
