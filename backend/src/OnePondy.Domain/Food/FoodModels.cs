namespace OnePondy.Domain.Food;

/// <summary>
/// The delivery areas a restaurant can name (spec Phase 8, decision F5). They are the same codes the customer app's area picker uses,
/// not map shapes, because no map provider is chosen (D6). <see cref="WholeCity"/> means every area.
/// </summary>
public static class FoodAreas
{
    public const string WholeCity = "puducherry";

    public static readonly IReadOnlyList<string> All =
    [
        WholeCity,
        "whiteTown",
        "tamilQuarter",
        "auroville",
        "lawspet",
        "muthialpet",
        "reddiarpalayam",
        "ariyankuppam",
        "villianur",
    ];

    public static bool IsKnown(string code) => All.Contains(code, StringComparer.Ordinal);

    /// <summary>Whether a restaurant that delivers to <paramref name="restaurantAreas"/> serves a customer in <paramref name="customerArea"/>.</summary>
    public static bool Serves(IEnumerable<string> restaurantAreas, string? customerArea)
    {
        // A customer who has not narrowed things down (null, or the whole city) sees every restaurant that delivers anywhere.
        if (customerArea is null or WholeCity)
        {
            return restaurantAreas.Any();
        }

        return restaurantAreas.Any(area => area == WholeCity || area == customerArea);
    }
}

/// <summary>
/// What a restaurant (a business of type restaurant) needs on top of its business record to take orders: how long it takes,
/// the smallest order it will deliver, whether it is open, and (in <see cref="RestaurantDeliveryArea"/>) where it delivers.
/// </summary>
public sealed class RestaurantProfile
{
    public const int MinPrepMinutes = 5;
    public const int MaxPrepMinutes = 180;
    public const decimal MaxOrderAmount = 100_000m;

    private RestaurantProfile()
    {
    }

    /// <summary>The restaurant's business: one profile per business.</summary>
    public Guid BusinessId { get; private set; }

    public int PrepTimeMinutes { get; private set; }

    public decimal MinOrderAmount { get; private set; }

    /// <summary>The "open" switch. A new restaurant starts closed, so nothing is orderable until the partner decides.</summary>
    public bool IsAcceptingOrders { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public static RestaurantProfile Create(Guid businessId, int prepTimeMinutes, decimal minOrderAmount, bool isAcceptingOrders, DateTimeOffset now)
    {
        var profile = new RestaurantProfile { BusinessId = businessId, CreatedAt = now };
        profile.Update(prepTimeMinutes, minOrderAmount, isAcceptingOrders, now);
        return profile;
    }

    public void Update(int prepTimeMinutes, decimal minOrderAmount, bool isAcceptingOrders, DateTimeOffset now)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(prepTimeMinutes, MinPrepMinutes);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(prepTimeMinutes, MaxPrepMinutes);
        ArgumentOutOfRangeException.ThrowIfNegative(minOrderAmount);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(minOrderAmount, MaxOrderAmount);

        PrepTimeMinutes = prepTimeMinutes;
        MinOrderAmount = decimal.Round(minOrderAmount, 2, MidpointRounding.AwayFromZero);
        IsAcceptingOrders = isAcceptingOrders;
        UpdatedAt = now;
    }
}

/// <summary>One area a restaurant delivers to. Replaced as a set whenever the partner saves.</summary>
public sealed class RestaurantDeliveryArea
{
    private RestaurantDeliveryArea()
    {
    }

    public Guid BusinessId { get; private set; }

    public string AreaCode { get; private set; } = string.Empty;

    public static RestaurantDeliveryArea Create(Guid businessId, string areaCode)
    {
        if (!FoodAreas.IsKnown(areaCode))
        {
            throw new ArgumentException("Unknown delivery area.", nameof(areaCode));
        }

        return new RestaurantDeliveryArea { BusinessId = businessId, AreaCode = areaCode };
    }
}

/// <summary>A heading on a restaurant's menu, such as "Starters".</summary>
public sealed class MenuCategory
{
    public const int MaxNameLength = 80;

    private MenuCategory()
    {
    }

    public Guid Id { get; private set; }

    public Guid BusinessId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public int Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public static MenuCategory Create(Guid businessId, string name, int position, DateTimeOffset now)
    {
        var category = new MenuCategory { Id = Guid.CreateVersion7(), BusinessId = businessId, CreatedAt = now };
        category.Update(name, position);
        return category;
    }

    public void Update(string name, int position)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentOutOfRangeException.ThrowIfNegative(position);
        Name = name.Trim();
        Position = position;
    }
}

/// <summary>
/// A dish. Never deleted once created, because orders will refer to it: a dish that is no longer sold is archived, and one that
/// is only out for today is marked unavailable.
/// </summary>
public sealed class MenuItem
{
    public const int MaxNameLength = 120;
    public const int MaxDescriptionLength = 500;
    public const decimal MaxPrice = 100_000m;

    /// <summary>Provisional GST rate for restaurant food until the tax review (decision F3); a partner can set another per dish.</summary>
    public const decimal DefaultTaxRatePercent = 5m;

    public const decimal MaxTaxRatePercent = 28m;

    private MenuItem()
    {
    }

    public Guid Id { get; private set; }

    public Guid BusinessId { get; private set; }

    public Guid CategoryId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    /// <summary>The price a customer pays for one, in rupees, before tax and fees.</summary>
    public decimal Price { get; private set; }

    public bool IsVeg { get; private set; }

    public decimal TaxRatePercent { get; private set; }

    /// <summary>False while the dish is sold out for now. It stays on the menu, greyed out.</summary>
    public bool IsAvailable { get; private set; }

    /// <summary>True once the dish has been taken off the menu for good. It is hidden from customers but orders keep their record of it.</summary>
    public bool IsArchived { get; private set; }

    public int Position { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public static MenuItem Create(Guid businessId, Guid categoryId, MenuItemContent content, int position, DateTimeOffset now)
    {
        var item = new MenuItem
        {
            Id = Guid.CreateVersion7(),
            BusinessId = businessId,
            CategoryId = categoryId,
            IsAvailable = true,
            Position = position,
            CreatedAt = now,
        };
        item.Apply(content, now);
        return item;
    }

    public void Update(Guid categoryId, MenuItemContent content, int position, DateTimeOffset now)
    {
        EnsureNotArchived();
        CategoryId = categoryId;
        Position = position;
        Apply(content, now);
    }

    public void SetAvailability(bool isAvailable, DateTimeOffset now)
    {
        EnsureNotArchived();
        IsAvailable = isAvailable;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        EnsureNotArchived();
        IsArchived = true;
        IsAvailable = false;
        UpdatedAt = now;
    }

    private void Apply(MenuItemContent content, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Name);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(content.Price, 0m);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(content.Price, MaxPrice);
        ArgumentOutOfRangeException.ThrowIfNegative(content.TaxRatePercent);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(content.TaxRatePercent, MaxTaxRatePercent);

        Name = content.Name.Trim();
        Description = string.IsNullOrWhiteSpace(content.Description) ? null : content.Description.Trim();
        Price = decimal.Round(content.Price, 2, MidpointRounding.AwayFromZero);
        IsVeg = content.IsVeg;
        TaxRatePercent = decimal.Round(content.TaxRatePercent, 2, MidpointRounding.AwayFromZero);
        UpdatedAt = now;
    }

    private void EnsureNotArchived()
    {
        if (IsArchived)
        {
            throw new InvalidOperationException("An archived dish cannot be changed.");
        }
    }
}

/// <summary>The partner-editable fields of a dish.</summary>
public sealed record MenuItemContent(string Name, string? Description, decimal Price, bool IsVeg, decimal TaxRatePercent);
