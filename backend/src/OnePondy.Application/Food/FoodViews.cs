using OnePondy.Application.Common;

namespace OnePondy.Application.Food;

public sealed record RestaurantProfileView(
    bool Configured,
    Guid BusinessId,
    int PrepTimeMinutes,
    decimal MinOrderAmount,
    bool IsAcceptingOrders,
    IReadOnlyList<string> DeliveryAreas,
    uint Version);

/// <summary>What a partner sends to set up or change their restaurant. <c>Version</c> is required once the profile exists.</summary>
public sealed record RestaurantInput(
    int PrepTimeMinutes,
    decimal MinOrderAmount,
    bool IsAcceptingOrders,
    IReadOnlyList<string>? DeliveryAreas,
    uint? Version);

public sealed record MenuItemView(
    Guid Id,
    Guid CategoryId,
    string Name,
    string? Description,
    decimal Price,
    string Currency,
    bool IsVeg,
    decimal TaxRatePercent,
    bool IsAvailable,
    bool IsArchived,
    int Position,
    uint Version);

public sealed record MenuCategoryView(Guid Id, string Name, int Position, uint Version, IReadOnlyList<MenuItemView> Items);

public sealed record MenuView(IReadOnlyList<MenuCategoryView> Categories);

public sealed record MenuCategoryInput(string? Name, int? Position, uint? Version);

public sealed record MenuItemInput(
    Guid? CategoryId,
    string? Name,
    string? Description,
    decimal? Price,
    bool IsVeg,
    decimal? TaxRatePercent,
    int? Position);

/// <summary>A restaurant as a customer sees it. The FSSAI number is shown for every restaurant (spec §21).</summary>
public sealed record PublicRestaurant(
    Guid BusinessId,
    string Name,
    string? Description,
    string? FssaiLicenseNumber,
    int PrepTimeMinutes,
    decimal MinOrderAmount,
    IReadOnlyList<string> DeliveryAreas);

/// <summary>An open restaurant's records, for ordering.</summary>
public sealed record OpenRestaurantInfo(OnePondy.Domain.Businesses.Business Business, OnePondy.Domain.Food.RestaurantProfile Profile, IReadOnlyList<string> Areas);

public sealed record PublicMenu(PublicRestaurant Restaurant, IReadOnlyList<MenuCategoryView> Categories);

public static class FoodErrors
{
    public static readonly AppError NotFound = new("restaurant_not_found", ErrorKind.NotFound, "That restaurant does not exist.");

    public static readonly AppError NotARestaurant = new(
        "business_not_a_restaurant",
        ErrorKind.Validation,
        "Only a business registered as a restaurant can have a menu and take orders.");

    public static readonly AppError BusinessNotActive = new(
        "restaurant_business_not_active",
        ErrorKind.Validation,
        "A restaurant can only open for orders once its business is approved and active.");

    public static readonly AppError Changed = new(
        "restaurant_changed",
        ErrorKind.Conflict,
        "This changed since you loaded it. Reload and try again.");

    public static readonly AppError MenuEmpty = new(
        "restaurant_menu_empty",
        ErrorKind.Validation,
        "Add at least one available dish before opening for orders.");

    public static readonly AppError CategoryNotFound = new("menu_category_not_found", ErrorKind.NotFound, "That menu category does not exist.");

    public static readonly AppError CategoryNotEmpty = new(
        "menu_category_not_empty",
        ErrorKind.Conflict,
        "This category still has dishes. Archive them and move any you keep before removing it.");

    public static readonly AppError ItemNotFound = new("menu_item_not_found", ErrorKind.NotFound, "That dish does not exist.");

    public static readonly AppError ItemArchived = new("menu_item_archived", ErrorKind.Conflict, "That dish has been taken off the menu and cannot be changed.");

    public static readonly AppError TooManyCategories = new(
        "menu_too_many_categories",
        ErrorKind.Validation,
        "A menu can have at most 30 categories.");

    public static readonly AppError TooManyItems = new(
        "menu_too_many_items",
        ErrorKind.Validation,
        "A menu can have at most 500 dishes. Archive dishes you no longer sell.");
}
