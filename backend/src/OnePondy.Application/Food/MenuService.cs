using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Food;

namespace OnePondy.Application.Food;

/// <summary>
/// A restaurant's menu (spec Phase 8, step 8.1): categories and dishes that only the restaurant's owner can change, and the
/// customer's read-only view of an open restaurant's menu. Dishes are never deleted, because orders will refer to them.
/// </summary>
public sealed class MenuService(IOnePondyDbContext db, RestaurantService restaurants, AuditTrail audit, TimeProvider time)
{
    public const int MaxCategories = 30;
    public const int MaxItems = 500;

    // --- Partner ---

    public async Task<Result<MenuView>> GetMineAsync(Guid businessId, Guid ownerUserId, bool includeArchived, CancellationToken cancellationToken)
    {
        var denied = await EnsureOwnedRestaurantAsync(businessId, ownerUserId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        return await BuildMenuAsync(businessId, includeArchived, availableOnly: false, cancellationToken);
    }

    public async Task<Result<MenuCategoryView>> CreateCategoryAsync(
        Guid businessId, Guid ownerUserId, MenuCategoryInput input, CancellationToken cancellationToken)
    {
        var denied = await EnsureOwnedRestaurantAsync(businessId, ownerUserId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var errors = new FieldErrors();
        var name = errors.Required("name", input.Name, MenuCategory.MaxNameLength);
        if (errors.Any)
        {
            return errors.ToError();
        }

        var existing = await db.MenuCategories.AsNoTracking().Where(c => c.BusinessId == businessId).ToListAsync(cancellationToken);
        if (existing.Count >= MaxCategories)
        {
            return FoodErrors.TooManyCategories;
        }

        if (existing.Any(c => string.Equals(c.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            return AppError.Validation(new Dictionary<string, string[]> { ["name"] = ["You already have a category with this name."] });
        }

        var position = input.Position ?? (existing.Count == 0 ? 0 : existing.Max(c => c.Position) + 1);
        var category = MenuCategory.Create(businessId, name, Math.Max(0, position), time.GetUtcNow());
        db.MenuCategories.Add(category);
        audit.Record(ownerUserId, AuditActions.MenuCategoryCreated, AuditEntityTypes.MenuCategory, category.Id, new { category.Name });
        return await SaveAsync(category, [], cancellationToken);
    }

    public async Task<Result<MenuCategoryView>> UpdateCategoryAsync(
        Guid businessId, Guid categoryId, Guid ownerUserId, MenuCategoryInput input, CancellationToken cancellationToken)
    {
        var denied = await EnsureOwnedRestaurantAsync(businessId, ownerUserId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var category = await db.MenuCategories.SingleOrDefaultAsync(c => c.Id == categoryId && c.BusinessId == businessId, cancellationToken);
        if (category is null)
        {
            return FoodErrors.CategoryNotFound;
        }

        var errors = new FieldErrors();
        var name = errors.Required("name", input.Name, MenuCategory.MaxNameLength);
        if (input.Version is null)
        {
            errors.Add("version", "Required.");
        }

        if (input.Position is < 0)
        {
            errors.Add("position", "Cannot be negative.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        var clash = await db.MenuCategories.AsNoTracking()
            .AnyAsync(c => c.BusinessId == businessId && c.Id != categoryId && c.Name.ToLower() == name.ToLower(), cancellationToken);
        if (clash)
        {
            return AppError.Validation(new Dictionary<string, string[]> { ["name"] = ["You already have a category with this name."] });
        }

        db.ExpectVersion(category, input.Version!.Value);
        category.Update(name, input.Position ?? category.Position);
        audit.Record(ownerUserId, AuditActions.MenuCategoryUpdated, AuditEntityTypes.MenuCategory, category.Id, new { category.Name });
        var items = await db.MenuItems.AsNoTracking().Where(i => i.CategoryId == categoryId && !i.IsArchived).ToListAsync(cancellationToken);
        return await SaveAsync(category, items, cancellationToken);
    }

    /// <summary>A category can only be removed once it holds no dishes at all, archived ones included (orders may still name them).</summary>
    public async Task<AppError?> DeleteCategoryAsync(Guid businessId, Guid categoryId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var denied = await EnsureOwnedRestaurantAsync(businessId, ownerUserId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var category = await db.MenuCategories.SingleOrDefaultAsync(c => c.Id == categoryId && c.BusinessId == businessId, cancellationToken);
        if (category is null)
        {
            return FoodErrors.CategoryNotFound;
        }

        if (await db.MenuItems.AsNoTracking().AnyAsync(i => i.CategoryId == categoryId, cancellationToken))
        {
            return FoodErrors.CategoryNotEmpty;
        }

        db.MenuCategories.Remove(category);
        audit.Record(ownerUserId, AuditActions.MenuCategoryDeleted, AuditEntityTypes.MenuCategory, category.Id, new { category.Name });
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return null;
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // A dish was added to it at the same moment.
            db.ClearTracked();
            return FoodErrors.CategoryNotEmpty;
        }
    }

    public async Task<Result<MenuItemView>> CreateItemAsync(Guid businessId, Guid ownerUserId, MenuItemInput input, CancellationToken cancellationToken)
    {
        var denied = await EnsureOwnedRestaurantAsync(businessId, ownerUserId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var content = ValidateItem(input, out var errors);
        if (content is null || errors.Any)
        {
            return errors.ToError();
        }

        if (input.CategoryId is not { } categoryId
            || !await db.MenuCategories.AsNoTracking().AnyAsync(c => c.Id == categoryId && c.BusinessId == businessId, cancellationToken))
        {
            return FoodErrors.CategoryNotFound;
        }

        var items = await db.MenuItems.AsNoTracking().Where(i => i.BusinessId == businessId && !i.IsArchived).ToListAsync(cancellationToken);
        if (items.Count >= MaxItems)
        {
            return FoodErrors.TooManyItems;
        }

        var position = input.Position ?? NextItemPosition(items, categoryId);
        var item = MenuItem.Create(businessId, categoryId, content, Math.Max(0, position), time.GetUtcNow());
        db.MenuItems.Add(item);
        audit.Record(ownerUserId, AuditActions.MenuItemCreated, AuditEntityTypes.MenuItem, item.Id, new { item.Name, item.Price });
        await db.SaveChangesAsync(cancellationToken);
        return ToView(item);
    }

    public async Task<Result<MenuItemView>> UpdateItemAsync(
        Guid businessId, Guid itemId, uint expectedVersion, Guid ownerUserId, MenuItemInput input, CancellationToken cancellationToken)
    {
        var denied = await EnsureOwnedRestaurantAsync(businessId, ownerUserId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var item = await db.MenuItems.SingleOrDefaultAsync(i => i.Id == itemId && i.BusinessId == businessId, cancellationToken);
        if (item is null)
        {
            return FoodErrors.ItemNotFound;
        }

        if (item.IsArchived)
        {
            return FoodErrors.ItemArchived;
        }

        var content = ValidateItem(input, out var errors);
        if (content is null || errors.Any)
        {
            return errors.ToError();
        }

        var categoryId = input.CategoryId ?? item.CategoryId;
        if (!await db.MenuCategories.AsNoTracking().AnyAsync(c => c.Id == categoryId && c.BusinessId == businessId, cancellationToken))
        {
            return FoodErrors.CategoryNotFound;
        }

        db.ExpectVersion(item, expectedVersion);
        item.Update(categoryId, content, Math.Max(0, input.Position ?? item.Position), time.GetUtcNow());
        audit.Record(ownerUserId, AuditActions.MenuItemUpdated, AuditEntityTypes.MenuItem, item.Id, new { item.Name, item.Price });
        return await SaveAsync(item, cancellationToken);
    }

    /// <summary>The sold-out switch. Flipped often, so it is not written to the audit log.</summary>
    public async Task<Result<MenuItemView>> SetAvailabilityAsync(
        Guid businessId, Guid itemId, uint expectedVersion, bool isAvailable, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var denied = await EnsureOwnedRestaurantAsync(businessId, ownerUserId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var item = await db.MenuItems.SingleOrDefaultAsync(i => i.Id == itemId && i.BusinessId == businessId, cancellationToken);
        if (item is null)
        {
            return FoodErrors.ItemNotFound;
        }

        if (item.IsArchived)
        {
            return FoodErrors.ItemArchived;
        }

        db.ExpectVersion(item, expectedVersion);
        item.SetAvailability(isAvailable, time.GetUtcNow());
        return await SaveAsync(item, cancellationToken);
    }

    public async Task<Result<MenuItemView>> ArchiveItemAsync(
        Guid businessId, Guid itemId, uint expectedVersion, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var denied = await EnsureOwnedRestaurantAsync(businessId, ownerUserId, cancellationToken);
        if (denied is not null)
        {
            return denied;
        }

        var item = await db.MenuItems.SingleOrDefaultAsync(i => i.Id == itemId && i.BusinessId == businessId, cancellationToken);
        if (item is null)
        {
            return FoodErrors.ItemNotFound;
        }

        if (item.IsArchived)
        {
            return FoodErrors.ItemArchived;
        }

        db.ExpectVersion(item, expectedVersion);
        item.Archive(time.GetUtcNow());
        audit.Record(ownerUserId, AuditActions.MenuItemArchived, AuditEntityTypes.MenuItem, item.Id, new { item.Name });
        return await SaveAsync(item, cancellationToken);
    }

    // --- Customers ---

    /// <summary>An open restaurant's menu. Archived dishes and empty categories are hidden; sold-out dishes show as unavailable.</summary>
    public async Task<Result<PublicMenu>> GetPublicAsync(Guid businessId, CancellationToken cancellationToken)
    {
        var restaurant = await restaurants.GetPublicAsync(businessId, cancellationToken);
        if (!restaurant.IsSuccess)
        {
            return restaurant.Error;
        }

        var menu = await BuildMenuAsync(businessId, includeArchived: false, availableOnly: false, cancellationToken);
        return new PublicMenu(restaurant.Value, [.. menu.Categories.Where(c => c.Items.Count > 0)]);
    }

    // --- Shared ---

    /// <summary>Null when the caller owns this restaurant; otherwise why not.</summary>
    private async Task<AppError?> EnsureOwnedRestaurantAsync(Guid businessId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var business = await db.Businesses.AsNoTracking().SingleOrDefaultAsync(b => b.Id == businessId, cancellationToken);
        if (business is null || business.OwnerUserId != ownerUserId)
        {
            return FoodErrors.NotFound;
        }

        return business.BusinessType == BusinessType.Restaurant ? null : FoodErrors.NotARestaurant;
    }

    private async Task<MenuView> BuildMenuAsync(Guid businessId, bool includeArchived, bool availableOnly, CancellationToken cancellationToken)
    {
        var categories = await db.MenuCategories.AsNoTracking()
            .Where(c => c.BusinessId == businessId)
            .OrderBy(c => c.Position)
            .ThenBy(c => c.Name)
            .ToListAsync(cancellationToken);
        var items = await db.MenuItems.AsNoTracking()
            .Where(i => i.BusinessId == businessId && (includeArchived || !i.IsArchived) && (!availableOnly || i.IsAvailable))
            .ToListAsync(cancellationToken);
        var byCategory = items.GroupBy(i => i.CategoryId).ToDictionary(g => g.Key, g => g.OrderBy(i => i.Position).ThenBy(i => i.Name).ToList());
        return new MenuView(
        [
            .. categories.Select(c => ToView(c, byCategory.GetValueOrDefault(c.Id, []))),
        ]);
    }

    private static MenuItemContent? ValidateItem(MenuItemInput input, out FieldErrors errors)
    {
        errors = new FieldErrors();
        var name = errors.Required("name", input.Name, MenuItem.MaxNameLength);
        var description = errors.Optional("description", input.Description, MenuItem.MaxDescriptionLength);
        if (input.Price is not > 0m or > MenuItem.MaxPrice)
        {
            errors.Add("price", $"Must be more than 0 and at most {MenuItem.MaxPrice:0}.");
        }

        var tax = input.TaxRatePercent ?? MenuItem.DefaultTaxRatePercent;
        if (tax is < 0m or > MenuItem.MaxTaxRatePercent)
        {
            errors.Add("taxRatePercent", $"Must be between 0 and {MenuItem.MaxTaxRatePercent:0}.");
        }

        if (input.Position is < 0)
        {
            errors.Add("position", "Cannot be negative.");
        }

        return errors.Any ? null : new MenuItemContent(name, description, input.Price!.Value, input.IsVeg, tax);
    }

    private static int NextItemPosition(IReadOnlyList<MenuItem> items, Guid categoryId)
    {
        var inCategory = items.Where(i => i.CategoryId == categoryId).ToList();
        return inCategory.Count == 0 ? 0 : inCategory.Max(i => i.Position) + 1;
    }

    private async Task<Result<MenuCategoryView>> SaveAsync(MenuCategory category, IReadOnlyList<MenuItem> items, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // A stale version, or the same category name added twice at the same moment.
            db.ClearTracked();
            return FoodErrors.Changed;
        }

        return ToView(category, items);
    }

    private async Task<Result<MenuItemView>> SaveAsync(MenuItem item, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            db.ClearTracked();
            return FoodErrors.Changed;
        }

        return ToView(item);
    }

    private static MenuCategoryView ToView(MenuCategory category, IReadOnlyList<MenuItem> items) =>
        new(category.Id, category.Name, category.Position, category.Version, [.. items.Select(ToView)]);

    private static MenuItemView ToView(MenuItem item) => new(
        item.Id,
        item.CategoryId,
        item.Name,
        item.Description,
        item.Price,
        "INR",
        item.IsVeg,
        item.TaxRatePercent,
        item.IsAvailable,
        item.IsArchived,
        item.Position,
        item.Version);
}
