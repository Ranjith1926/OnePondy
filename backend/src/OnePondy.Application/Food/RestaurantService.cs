using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Food;

namespace OnePondy.Application.Food;

/// <summary>
/// A restaurant's profile and its public listing (spec Phase 8, step 8.1). A partner sets up and opens their own restaurant;
/// customers see only restaurants that are open, approved, active and deliver to their area.
/// </summary>
public sealed class RestaurantService(IOnePondyDbContext db, AuditTrail audit, TimeProvider time)
{
    /// <summary>The most restaurants one public list returns.</summary>
    public const int MaxPublicRestaurants = 200;

    // --- Partner ---

    public async Task<Result<RestaurantProfileView>> GetMineAsync(Guid businessId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var business = await FindOwnedRestaurantAsync(businessId, ownerUserId, cancellationToken);
        if (!business.IsSuccess)
        {
            return business.Error;
        }

        var profile = await db.RestaurantProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.BusinessId == businessId, cancellationToken);
        return profile is null ? NotConfigured(businessId) : await ToViewAsync(profile, cancellationToken);
    }

    /// <summary>Creates the profile the first time and updates it after. Opening for orders needs an active business, an area and a dish.</summary>
    public async Task<Result<RestaurantProfileView>> SaveMineAsync(
        Guid businessId, Guid ownerUserId, RestaurantInput input, CancellationToken cancellationToken)
    {
        var owned = await FindOwnedRestaurantAsync(businessId, ownerUserId, cancellationToken);
        if (!owned.IsSuccess)
        {
            return owned.Error;
        }

        var errors = new FieldErrors();
        if (input.PrepTimeMinutes is < RestaurantProfile.MinPrepMinutes or > RestaurantProfile.MaxPrepMinutes)
        {
            errors.Add("prepTimeMinutes", $"Must be between {RestaurantProfile.MinPrepMinutes} and {RestaurantProfile.MaxPrepMinutes}.");
        }

        if (input.MinOrderAmount is < 0m or > RestaurantProfile.MaxOrderAmount)
        {
            errors.Add("minOrderAmount", $"Must be between 0 and {RestaurantProfile.MaxOrderAmount:0}.");
        }

        var areas = (input.DeliveryAreas ?? []).Distinct(StringComparer.Ordinal).ToList();
        if (areas.Any(area => !FoodAreas.IsKnown(area)))
        {
            errors.Add("deliveryAreas", "Choose from the listed areas.");
        }

        if (input.IsAcceptingOrders && areas.Count == 0)
        {
            errors.Add("deliveryAreas", "Choose at least one area before opening for orders.");
        }

        if (errors.Any)
        {
            return errors.ToError();
        }

        if (input.IsAcceptingOrders)
        {
            if (!owned.Value.IsActive)
            {
                return FoodErrors.BusinessNotActive;
            }

            var hasDish = await db.MenuItems.AsNoTracking()
                .AnyAsync(i => i.BusinessId == businessId && i.IsAvailable && !i.IsArchived, cancellationToken);
            if (!hasDish)
            {
                return FoodErrors.MenuEmpty;
            }
        }

        var now = time.GetUtcNow();
        var profile = await db.RestaurantProfiles.SingleOrDefaultAsync(p => p.BusinessId == businessId, cancellationToken);
        if (profile is null)
        {
            profile = RestaurantProfile.Create(businessId, input.PrepTimeMinutes, input.MinOrderAmount, input.IsAcceptingOrders, now);
            db.RestaurantProfiles.Add(profile);
        }
        else
        {
            if (input.Version is not { } version)
            {
                return AppError.Validation(new Dictionary<string, string[]> { ["version"] = ["Required."] });
            }

            db.ExpectVersion(profile, version);
            profile.Update(input.PrepTimeMinutes, input.MinOrderAmount, input.IsAcceptingOrders, now);
        }

        var existing = await db.RestaurantDeliveryAreas.Where(a => a.BusinessId == businessId).ToListAsync(cancellationToken);
        db.RestaurantDeliveryAreas.RemoveRange(existing.Where(a => !areas.Contains(a.AreaCode)));
        foreach (var area in areas.Where(code => existing.All(a => a.AreaCode != code)))
        {
            db.RestaurantDeliveryAreas.Add(RestaurantDeliveryArea.Create(businessId, area));
        }

        audit.Record(
            ownerUserId,
            AuditActions.RestaurantUpdated,
            AuditEntityTypes.Restaurant,
            businessId,
            new { input.PrepTimeMinutes, input.MinOrderAmount, input.IsAcceptingOrders, areas });

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception ex) when (WriteConflicts.Is(ex))
        {
            // Someone saved at the same moment (a stale version, or both creating the first profile).
            db.ClearTracked();
            return FoodErrors.Changed;
        }

        return await ToViewAsync(profile, cancellationToken);
    }

    // --- Customers ---

    /// <summary>Restaurants open for orders that deliver to <paramref name="area"/> (null or the whole city: any), by name.</summary>
    public async Task<IReadOnlyList<PublicRestaurant>> ListPublicAsync(string? area, CancellationToken cancellationToken)
    {
        var open = await OpenRestaurantsQuery(null).ToListAsync(cancellationToken);
        var areasByBusiness = await AreasOfAsync(open.Select(o => o.Business.Id).ToList(), cancellationToken);
        return
        [
            .. open
                .Select(o => ToPublic(o.Business, o.Profile, areasByBusiness.GetValueOrDefault(o.Business.Id, [])))
                .Where(r => FoodAreas.Serves(r.DeliveryAreas, area))
                .Take(MaxPublicRestaurants),
        ];
    }

    /// <summary>One restaurant, only while it is open to customers; anything else answers not found so a closed one is not revealed.</summary>
    public async Task<Result<PublicRestaurant>> GetPublicAsync(Guid businessId, CancellationToken cancellationToken)
    {
        var found = await OpenRestaurantsQuery(businessId).SingleOrDefaultAsync(cancellationToken);
        if (found is null)
        {
            return FoodErrors.NotFound;
        }

        var areas = await AreasOfAsync([businessId], cancellationToken);
        return ToPublic(found.Business, found.Profile, areas.GetValueOrDefault(businessId, []));
    }

    // --- Shared ---

    private sealed record OpenRestaurant(Business Business, RestaurantProfile Profile);

    /// <summary>The restaurants customers can see, or just one of them. The filter is inside the join because EF cannot filter after projecting it.</summary>
    private IQueryable<OpenRestaurant> OpenRestaurantsQuery(Guid? businessId) =>
        from profile in db.RestaurantProfiles.AsNoTracking()
        join business in db.Businesses.AsNoTracking() on profile.BusinessId equals business.Id
        where profile.IsAcceptingOrders
            && business.BusinessType == BusinessType.Restaurant
            && business.ApprovalStatus == BusinessApprovalStatus.Approved
            && business.IsActive
            && (businessId == null || business.Id == businessId)
        orderby business.Name
        select new OpenRestaurant(business, profile);

    private async Task<Dictionary<Guid, List<string>>> AreasOfAsync(List<Guid> businessIds, CancellationToken cancellationToken)
    {
        var rows = await db.RestaurantDeliveryAreas.AsNoTracking()
            .Where(a => businessIds.Contains(a.BusinessId))
            .ToListAsync(cancellationToken);
        return rows.GroupBy(a => a.BusinessId).ToDictionary(g => g.Key, g => g.Select(a => a.AreaCode).Order(StringComparer.Ordinal).ToList());
    }

    private async Task<Result<Business>> FindOwnedRestaurantAsync(Guid businessId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var business = await db.Businesses.AsNoTracking().SingleOrDefaultAsync(b => b.Id == businessId, cancellationToken);
        if (business is null || business.OwnerUserId != ownerUserId)
        {
            return FoodErrors.NotFound;
        }

        return business.BusinessType == BusinessType.Restaurant ? business : FoodErrors.NotARestaurant;
    }

    private async Task<RestaurantProfileView> ToViewAsync(RestaurantProfile profile, CancellationToken cancellationToken)
    {
        var areas = await AreasOfAsync([profile.BusinessId], cancellationToken);
        return new RestaurantProfileView(
            true,
            profile.BusinessId,
            profile.PrepTimeMinutes,
            profile.MinOrderAmount,
            profile.IsAcceptingOrders,
            areas.GetValueOrDefault(profile.BusinessId, []),
            profile.Version);
    }

    private static RestaurantProfileView NotConfigured(Guid businessId) =>
        new(false, businessId, 30, 0m, false, [], 0);

    private static PublicRestaurant ToPublic(Business business, RestaurantProfile profile, IReadOnlyList<string> areas) => new(
        business.Id,
        business.Name,
        business.Description,
        business.FssaiLicenseNumber,
        profile.PrepTimeMinutes,
        profile.MinOrderAmount,
        areas);
}
