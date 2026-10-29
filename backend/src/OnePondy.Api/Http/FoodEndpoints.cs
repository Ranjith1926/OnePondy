using System.Security.Claims;
using OnePondy.Application.Common;
using OnePondy.Application.Food;
using OnePondy.Application.Identity;

namespace OnePondy.Api.Http;

public sealed record SaveRestaurantBody(int PrepTimeMinutes, decimal MinOrderAmount, bool IsAcceptingOrders, string[]? DeliveryAreas, uint? Version);

public sealed record MenuCategoryBody(string? Name, int? Position, uint? Version);

public sealed record MenuItemBody(Guid? CategoryId, string? Name, string? Description, decimal? Price, bool IsVeg, decimal? TaxRatePercent, int? Position, uint Version);

public sealed record ItemAvailabilityBody(bool IsAvailable, uint Version);

public sealed record ItemVersionBody(uint Version);

/// <summary>
/// Restaurants and menus (spec Phase 8, step 8.1). A partner sets up and opens their own restaurant and keeps its menu; customers
/// browse open restaurants by area and read a menu. Ordering arrives in the next step.
/// </summary>
public static class FoodEndpoints
{
    public static IEndpointRouteBuilder MapFoodEndpoints(this IEndpointRouteBuilder app)
    {
        var partner = app.MapGroup("/api/v1/partner/businesses/{businessId:guid}").RequireAuthorization().WithTags("Partner restaurant");
        partner.MapGet("/restaurant", GetMine);
        partner.MapPut("/restaurant", SaveMine);
        partner.MapGet("/menu", GetMenu);
        partner.MapPost("/menu/categories", CreateCategory);
        partner.MapPut("/menu/categories/{categoryId:guid}", UpdateCategory);
        partner.MapDelete("/menu/categories/{categoryId:guid}", DeleteCategory);
        partner.MapPost("/menu/items", CreateItem);
        partner.MapPut("/menu/items/{itemId:guid}", UpdateItem);
        partner.MapPost("/menu/items/{itemId:guid}/availability", SetAvailability);
        partner.MapPost("/menu/items/{itemId:guid}/archive", ArchiveItem);

        var pub = app.MapGroup("/api/v1/restaurants").WithTags("Restaurants");
        pub.MapGet("/", ListPublic);
        pub.MapGet("/{businessId:guid}", GetPublic);
        pub.MapGet("/{businessId:guid}/menu", GetPublicMenu);
        return app;
    }

    private static async Task<IResult> GetMine(Guid businessId, ClaimsPrincipal user, RestaurantService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetMineAsync(businessId, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> SaveMine(
        Guid businessId, SaveRestaurantBody body, ClaimsPrincipal user, RestaurantService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(
            await service.SaveMineAsync(
                businessId, user.GetUserId(), new RestaurantInput(body.PrepTimeMinutes, body.MinOrderAmount, body.IsAcceptingOrders, body.DeliveryAreas, body.Version), ct),
            httpContext);

    private static async Task<IResult> GetMenu(
        Guid businessId, bool? includeArchived, ClaimsPrincipal user, MenuService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetMineAsync(businessId, user.GetUserId(), includeArchived ?? false, ct), httpContext);

    private static async Task<IResult> CreateCategory(
        Guid businessId, MenuCategoryBody body, ClaimsPrincipal user, MenuService service, HttpContext httpContext, CancellationToken ct)
    {
        var result = await service.CreateCategoryAsync(businessId, user.GetUserId(), new MenuCategoryInput(body.Name, body.Position, body.Version), ct);
        return result.IsSuccess
            ? Results.Created($"/api/v1/partner/businesses/{businessId}/menu", result.Value)
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> UpdateCategory(
        Guid businessId, Guid categoryId, MenuCategoryBody body, ClaimsPrincipal user, MenuService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(
            await service.UpdateCategoryAsync(businessId, categoryId, user.GetUserId(), new MenuCategoryInput(body.Name, body.Position, body.Version), ct),
            httpContext);

    private static async Task<IResult> DeleteCategory(
        Guid businessId, Guid categoryId, ClaimsPrincipal user, MenuService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.NoContent(await service.DeleteCategoryAsync(businessId, categoryId, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> CreateItem(
        Guid businessId, MenuItemBody body, ClaimsPrincipal user, MenuService service, HttpContext httpContext, CancellationToken ct)
    {
        var result = await service.CreateItemAsync(businessId, user.GetUserId(), ToInput(body), ct);
        return result.IsSuccess
            ? Results.Created($"/api/v1/partner/businesses/{businessId}/menu", result.Value)
            : ProblemResults.From(result.Error, httpContext);
    }

    private static async Task<IResult> UpdateItem(
        Guid businessId, Guid itemId, MenuItemBody body, ClaimsPrincipal user, MenuService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.UpdateItemAsync(businessId, itemId, body.Version, user.GetUserId(), ToInput(body), ct), httpContext);

    private static async Task<IResult> SetAvailability(
        Guid businessId, Guid itemId, ItemAvailabilityBody body, ClaimsPrincipal user, MenuService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.SetAvailabilityAsync(businessId, itemId, body.Version, body.IsAvailable, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> ArchiveItem(
        Guid businessId, Guid itemId, ItemVersionBody body, ClaimsPrincipal user, MenuService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.ArchiveItemAsync(businessId, itemId, body.Version, user.GetUserId(), ct), httpContext);

    private static async Task<IResult> ListPublic(string? area, RestaurantService service, HttpContext httpContext, CancellationToken ct)
    {
        if (area is not null && !OnePondy.Domain.Food.FoodAreas.IsKnown(area))
        {
            return ProblemResults.From(
                AppError.Validation(new Dictionary<string, string[]> { ["area"] = ["Unknown area."] }),
                httpContext);
        }

        return Results.Ok(await service.ListPublicAsync(area, ct));
    }

    private static async Task<IResult> GetPublic(Guid businessId, RestaurantService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetPublicAsync(businessId, ct), httpContext);

    private static async Task<IResult> GetPublicMenu(Guid businessId, MenuService service, HttpContext httpContext, CancellationToken ct) =>
        EndpointResults.Ok(await service.GetPublicAsync(businessId, ct), httpContext);

    private static MenuItemInput ToInput(MenuItemBody body) =>
        new(body.CategoryId, body.Name, body.Description, body.Price, body.IsVeg, body.TaxRatePercent, body.Position);
}
