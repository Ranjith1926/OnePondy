using OnePondy.Application.Audit;
using OnePondy.Application.Businesses;
using OnePondy.Application.Common;
using OnePondy.Application.Food;
using OnePondy.Application.Identity;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Food;
using OnePondy.Domain.Identity;
using OnePondy.UnitTests.Businesses;
using OnePondy.UnitTests.Identity;

namespace OnePondy.UnitTests.Food;

/// <summary>Phase 8 step 8.1: restaurants and menus, on the in-memory database with a controllable clock.</summary>
public class FoodTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly BusinessHarness _b = new();
    private readonly RestaurantService _restaurants;
    private readonly MenuService _menu;
    private readonly Guid _owner;
    private readonly Guid _someoneElse;

    public FoodTests()
    {
        var audit = new AuditTrail(_b.Db, _b.Time);
        _restaurants = new RestaurantService(_b.Db, audit, _b.Time);
        _menu = new MenuService(_b.Db, _restaurants, audit, _b.Time);
        _owner = _b.Customer;
        _someoneElse = _b.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000011111", None).Result.Id;
    }

    // --- Setup helpers ---

    /// <summary>A restaurant business owned by <see cref="_owner"/>, approved unless told otherwise.</summary>
    private async Task<Guid> RestaurantBusinessAsync(string name = "Spice Route", bool approved = true, BusinessType type = BusinessType.Restaurant)
    {
        var applied = await _b.Businesses.ApplyAsync(_owner, BusinessHarness.Input(name, type, type == BusinessType.Restaurant ? "12345678901234" : null), None);
        var detail = applied.Value!;
        if (approved)
        {
            detail = (await _b.Businesses.ApproveAsync(detail.Id, detail.Version, _b.Administrator, None)).Value!;
        }

        return detail.Id;
    }

    private static MenuItemInput Dish(Guid? categoryId, string name = "Masala dosa", decimal price = 120m) =>
        new(categoryId, name, "Crisp, with sambar and chutneys.", price, true, null, null);

    private async Task<Guid> CategoryAsync(Guid businessId, string name = "Breakfast") =>
        (await _menu.CreateCategoryAsync(businessId, _owner, new MenuCategoryInput(name, null, null), None)).Value!.Id;

    private async Task<(Guid Category, MenuItemView Item)> DishAsync(Guid businessId, string name = "Masala dosa", decimal price = 120m)
    {
        var category = await CategoryAsync(businessId);
        var item = (await _menu.CreateItemAsync(businessId, _owner, Dish(category, name, price), None)).Value!;
        return (category, item);
    }

    private static RestaurantInput Open(params string[] areas) => new(30, 100m, true, areas, null);

    private async Task<RestaurantProfileView> OpenForOrdersAsync(Guid businessId, params string[] areas)
    {
        await DishAsync(businessId);
        return (await _restaurants.SaveMineAsync(businessId, _owner, Open(areas.Length == 0 ? [FoodAreas.WholeCity] : areas), None)).Value!;
    }

    // --- Who may manage a restaurant ---

    [Fact]
    public async Task Only_the_owner_of_a_restaurant_business_can_see_or_change_its_restaurant_and_menu()
    {
        var id = await RestaurantBusinessAsync();

        Assert.Equal("restaurant_not_found", (await _restaurants.GetMineAsync(id, _someoneElse, None)).Error!.Code);
        Assert.Equal("restaurant_not_found", (await _restaurants.SaveMineAsync(id, _someoneElse, Open("whiteTown"), None)).Error!.Code);
        Assert.Equal("restaurant_not_found", (await _menu.GetMineAsync(id, _someoneElse, false, None)).Error!.Code);
        Assert.Equal("restaurant_not_found", (await _menu.CreateCategoryAsync(id, _someoneElse, new MenuCategoryInput("Mains", null, null), None)).Error!.Code);
        Assert.Equal("restaurant_not_found", (await _restaurants.GetMineAsync(Guid.NewGuid(), _owner, None)).Error!.Code);
    }

    [Fact]
    public async Task A_business_that_is_not_a_restaurant_cannot_have_a_menu()
    {
        var hotel = await RestaurantBusinessAsync("Sea View", type: BusinessType.Hotel);

        Assert.Equal("business_not_a_restaurant", (await _restaurants.GetMineAsync(hotel, _owner, None)).Error!.Code);
        Assert.Equal("business_not_a_restaurant", (await _menu.CreateCategoryAsync(hotel, _owner, new MenuCategoryInput("Mains", null, null), None)).Error!.Code);
    }

    // --- The restaurant profile ---

    [Fact]
    public async Task A_new_restaurant_starts_unconfigured_and_closed()
    {
        var id = await RestaurantBusinessAsync();

        var profile = (await _restaurants.GetMineAsync(id, _owner, None)).Value!;

        Assert.False(profile.Configured);
        Assert.False(profile.IsAcceptingOrders);
        Assert.Empty(profile.DeliveryAreas);
    }

    [Fact]
    public async Task A_partner_can_set_up_their_restaurant_and_change_it_with_the_version()
    {
        var id = await RestaurantBusinessAsync();

        var created = (await _restaurants.SaveMineAsync(id, _owner, new RestaurantInput(25, 150m, false, ["whiteTown", "auroville"], null), None)).Value!;
        Assert.True(created.Configured);
        Assert.Equal((25, 150m, false), (created.PrepTimeMinutes, created.MinOrderAmount, created.IsAcceptingOrders));
        Assert.Equal(["auroville", "whiteTown"], created.DeliveryAreas);

        var changed = (await _restaurants.SaveMineAsync(id, _owner, new RestaurantInput(40, 0m, false, ["lawspet"], created.Version), None)).Value!;
        Assert.Equal((40, 0m), (changed.PrepTimeMinutes, changed.MinOrderAmount));
        Assert.Equal(["lawspet"], changed.DeliveryAreas);
    }

    [Fact]
    public async Task Changing_an_existing_restaurant_needs_its_version()
    {
        var id = await RestaurantBusinessAsync();
        await _restaurants.SaveMineAsync(id, _owner, new RestaurantInput(30, 0m, false, ["whiteTown"], null), None);

        var result = await _restaurants.SaveMineAsync(id, _owner, new RestaurantInput(30, 0m, false, ["whiteTown"], null), None);

        Assert.Equal(ErrorKind.Validation, result.Error!.Kind);
        Assert.Contains("version", result.Error.FieldErrors!.Keys);
    }

    [Theory]
    [InlineData(4, 0, "prepTimeMinutes")]
    [InlineData(181, 0, "prepTimeMinutes")]
    [InlineData(30, -1, "minOrderAmount")]
    [InlineData(30, 100001, "minOrderAmount")]
    public async Task The_numbers_on_a_restaurant_must_be_sensible(int prep, int minimum, string field)
    {
        var id = await RestaurantBusinessAsync();

        var result = await _restaurants.SaveMineAsync(id, _owner, new RestaurantInput(prep, minimum, false, ["whiteTown"], null), None);

        Assert.Contains(field, result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Only_the_listed_areas_are_accepted()
    {
        var id = await RestaurantBusinessAsync();

        var result = await _restaurants.SaveMineAsync(id, _owner, new RestaurantInput(30, 0m, false, ["whiteTown", "narnia"], null), None);

        Assert.Contains("deliveryAreas", result.Error!.FieldErrors!.Keys);
    }

    // --- Opening for orders ---

    [Fact]
    public async Task A_restaurant_cannot_open_without_a_delivery_area()
    {
        var id = await RestaurantBusinessAsync();
        await DishAsync(id);

        var result = await _restaurants.SaveMineAsync(id, _owner, new RestaurantInput(30, 0m, true, [], null), None);

        Assert.Contains("deliveryAreas", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task A_restaurant_cannot_open_without_an_available_dish()
    {
        var id = await RestaurantBusinessAsync();

        var empty = await _restaurants.SaveMineAsync(id, _owner, Open("whiteTown"), None);
        var (_, item) = await DishAsync(id);
        await _menu.SetAvailabilityAsync(id, item.Id, item.Version, false, _owner, None);
        var soldOut = await _restaurants.SaveMineAsync(id, _owner, Open("whiteTown"), None);

        Assert.Equal("restaurant_menu_empty", empty.Error!.Code);
        Assert.Equal("restaurant_menu_empty", soldOut.Error!.Code);
    }

    [Fact]
    public async Task A_restaurant_whose_business_is_not_approved_cannot_open_but_can_prepare()
    {
        var id = await RestaurantBusinessAsync(approved: false);
        await DishAsync(id);

        var open = await _restaurants.SaveMineAsync(id, _owner, Open("whiteTown"), None);
        var prepared = await _restaurants.SaveMineAsync(id, _owner, new RestaurantInput(30, 0m, false, ["whiteTown"], null), None);

        Assert.Equal("restaurant_business_not_active", open.Error!.Code);
        Assert.True(prepared.IsSuccess);
    }

    // --- Menu categories ---

    [Fact]
    public async Task Categories_are_ordered_and_a_name_cannot_repeat_whatever_the_capitals()
    {
        var id = await RestaurantBusinessAsync();
        await CategoryAsync(id, "Starters");
        await CategoryAsync(id, "Mains");

        var again = await _menu.CreateCategoryAsync(id, _owner, new MenuCategoryInput("STARTERS", null, null), None);
        var menu = (await _menu.GetMineAsync(id, _owner, false, None)).Value!;

        Assert.Contains("name", again.Error!.FieldErrors!.Keys);
        Assert.Equal(["Starters", "Mains"], menu.Categories.Select(c => c.Name));
        Assert.Equal([0, 1], menu.Categories.Select(c => c.Position));
    }

    [Fact]
    public async Task A_category_can_be_renamed_with_its_version_but_not_to_another_categorys_name()
    {
        var id = await RestaurantBusinessAsync();
        var first = (await _menu.CreateCategoryAsync(id, _owner, new MenuCategoryInput("Starters", null, null), None)).Value!;
        await CategoryAsync(id, "Mains");

        var clash = await _menu.UpdateCategoryAsync(id, first.Id, _owner, new MenuCategoryInput("mains", null, first.Version), None);
        var renamed = await _menu.UpdateCategoryAsync(id, first.Id, _owner, new MenuCategoryInput("Small plates", 5, first.Version), None);

        Assert.Contains("name", clash.Error!.FieldErrors!.Keys);
        Assert.Equal(("Small plates", 5), (renamed.Value!.Name, renamed.Value.Position));
    }

    [Fact]
    public async Task A_category_that_has_held_a_dish_cannot_be_deleted_but_an_empty_one_can()
    {
        var id = await RestaurantBusinessAsync();
        var (category, item) = await DishAsync(id);
        var empty = await CategoryAsync(id, "Desserts");

        var held = await _menu.DeleteCategoryAsync(id, category, _owner, None);
        await _menu.ArchiveItemAsync(id, item.Id, item.Version, _owner, None);
        var heldEvenArchived = await _menu.DeleteCategoryAsync(id, category, _owner, None);
        var removed = await _menu.DeleteCategoryAsync(id, empty, _owner, None);

        Assert.Equal("menu_category_not_empty", held!.Code);
        Assert.Equal("menu_category_not_empty", heldEvenArchived!.Code);
        Assert.Null(removed);
    }

    [Fact]
    public async Task A_menu_has_a_limit_on_categories()
    {
        var id = await RestaurantBusinessAsync();
        for (var n = 0; n < MenuService.MaxCategories; n++)
        {
            await CategoryAsync(id, $"Category {n}");
        }

        var result = await _menu.CreateCategoryAsync(id, _owner, new MenuCategoryInput("One too many", null, null), None);

        Assert.Equal("menu_too_many_categories", result.Error!.Code);
    }

    // --- Dishes ---

    [Fact]
    public async Task A_dish_is_created_available_with_the_provisional_tax_rate()
    {
        var id = await RestaurantBusinessAsync();

        var (category, item) = await DishAsync(id, "Idli", 60m);

        Assert.Equal((category, "Idli", 60m, true, true, false), (item.CategoryId, item.Name, item.Price, item.IsVeg, item.IsAvailable, item.IsArchived));
        Assert.Equal(MenuItem.DefaultTaxRatePercent, item.TaxRatePercent);
        Assert.Equal("INR", item.Currency);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(100001)]
    public async Task A_dish_needs_a_sensible_price(double price)
    {
        var id = await RestaurantBusinessAsync();
        var category = await CategoryAsync(id);

        var result = await _menu.CreateItemAsync(id, _owner, Dish(category, price: (decimal)price), None);

        Assert.Contains("price", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task A_dish_needs_a_name_and_a_tax_rate_within_limits()
    {
        var id = await RestaurantBusinessAsync();
        var category = await CategoryAsync(id);

        var result = await _menu.CreateItemAsync(id, _owner, new MenuItemInput(category, " ", null, 50m, false, 40m, null), None);

        Assert.Contains("name", result.Error!.FieldErrors!.Keys);
        Assert.Contains("taxRatePercent", result.Error.FieldErrors.Keys);
    }

    [Fact]
    public async Task A_dish_must_go_in_one_of_the_restaurants_own_categories()
    {
        var mine = await RestaurantBusinessAsync("Mine");
        var theirs = await RestaurantBusinessAsync("Theirs");
        var theirCategory = await CategoryAsync(theirs);

        var elsewhere = await _menu.CreateItemAsync(mine, _owner, Dish(theirCategory), None);
        var nowhere = await _menu.CreateItemAsync(mine, _owner, Dish(null), None);

        Assert.Equal("menu_category_not_found", elsewhere.Error!.Code);
        Assert.Equal("menu_category_not_found", nowhere.Error!.Code);
    }

    [Fact]
    public async Task A_dish_can_be_edited_and_moved_with_its_version()
    {
        var id = await RestaurantBusinessAsync();
        var (_, item) = await DishAsync(id);
        var other = await CategoryAsync(id, "Mains");

        var changed = (await _menu.UpdateItemAsync(id, item.Id, item.Version, _owner, new MenuItemInput(other, "Ghee dosa", null, 150m, true, 12m, 3), None)).Value!;

        Assert.Equal((other, "Ghee dosa", 150m, 12m, 3), (changed.CategoryId, changed.Name, changed.Price, changed.TaxRatePercent, changed.Position));
        Assert.Null(changed.Description);
    }

    [Fact]
    public async Task Sold_out_is_a_switch_and_a_sold_out_dish_stays_on_the_menu()
    {
        var id = await RestaurantBusinessAsync();
        var (_, item) = await DishAsync(id);

        var off = (await _menu.SetAvailabilityAsync(id, item.Id, item.Version, false, _owner, None)).Value!;
        var menu = (await _menu.GetMineAsync(id, _owner, false, None)).Value!;
        var on = (await _menu.SetAvailabilityAsync(id, item.Id, off.Version, true, _owner, None)).Value!;

        Assert.False(off.IsAvailable);
        Assert.False(menu.Categories.Single().Items.Single().IsAvailable);
        Assert.True(on.IsAvailable);
    }

    [Fact]
    public async Task An_archived_dish_leaves_the_menu_but_is_kept_and_cannot_be_changed()
    {
        var id = await RestaurantBusinessAsync();
        var (_, item) = await DishAsync(id);

        var archived = (await _menu.ArchiveItemAsync(id, item.Id, item.Version, _owner, None)).Value!;
        var hidden = (await _menu.GetMineAsync(id, _owner, false, None)).Value!;
        var shown = (await _menu.GetMineAsync(id, _owner, true, None)).Value!;
        var edit = await _menu.UpdateItemAsync(id, item.Id, archived.Version, _owner, Dish(item.CategoryId), None);
        var flip = await _menu.SetAvailabilityAsync(id, item.Id, archived.Version, true, _owner, None);

        Assert.True(archived.IsArchived);
        Assert.False(archived.IsAvailable);
        Assert.Empty(hidden.Categories.Single().Items);
        Assert.True(shown.Categories.Single().Items.Single().IsArchived);
        Assert.Equal("menu_item_archived", edit.Error!.Code);
        Assert.Equal("menu_item_archived", flip.Error!.Code);
    }

    [Fact]
    public async Task One_restaurant_cannot_touch_another_restaurants_dish()
    {
        var mine = await RestaurantBusinessAsync("Mine");
        var theirs = await RestaurantBusinessAsync("Theirs");
        var (_, theirDish) = await DishAsync(theirs);

        var result = await _menu.ArchiveItemAsync(mine, theirDish.Id, theirDish.Version, _owner, None);

        Assert.Equal("menu_item_not_found", result.Error!.Code);
    }

    // --- What customers see ---

    [Fact]
    public async Task A_closed_restaurant_is_invisible_to_customers()
    {
        var id = await RestaurantBusinessAsync();
        await DishAsync(id);
        await _restaurants.SaveMineAsync(id, _owner, new RestaurantInput(30, 0m, false, ["whiteTown"], null), None);

        Assert.Empty(await _restaurants.ListPublicAsync(null, None));
        Assert.Equal("restaurant_not_found", (await _restaurants.GetPublicAsync(id, None)).Error!.Code);
        Assert.Equal("restaurant_not_found", (await _menu.GetPublicAsync(id, None)).Error!.Code);
    }

    [Fact]
    public async Task An_open_restaurant_is_listed_with_its_fssai_number_and_menu()
    {
        var id = await RestaurantBusinessAsync("Spice Route");
        await OpenForOrdersAsync(id, "whiteTown");

        var listed = Assert.Single(await _restaurants.ListPublicAsync("whiteTown", None));
        var menu = (await _menu.GetPublicAsync(id, None)).Value!;

        Assert.Equal(("Spice Route", "12345678901234", 30, 100m), (listed.Name, listed.FssaiLicenseNumber, listed.PrepTimeMinutes, listed.MinOrderAmount));
        Assert.Equal(["whiteTown"], listed.DeliveryAreas);
        Assert.Equal("Masala dosa", menu.Categories.Single().Items.Single().Name);
        Assert.Equal(id, menu.Restaurant.BusinessId);
    }

    [Fact]
    public async Task Restaurants_are_listed_by_the_customers_area()
    {
        var white = await RestaurantBusinessAsync("White Town Cafe");
        var everywhere = await RestaurantBusinessAsync("City Kitchen");
        await OpenForOrdersAsync(white, "whiteTown");
        await OpenForOrdersAsync(everywhere, FoodAreas.WholeCity);

        var inWhiteTown = await _restaurants.ListPublicAsync("whiteTown", None);
        var inAuroville = await _restaurants.ListPublicAsync("auroville", None);
        var anywhere = await _restaurants.ListPublicAsync(null, None);
        var wholeCity = await _restaurants.ListPublicAsync(FoodAreas.WholeCity, None);

        Assert.Equal(["City Kitchen", "White Town Cafe"], inWhiteTown.Select(r => r.Name));
        Assert.Equal(["City Kitchen"], inAuroville.Select(r => r.Name));
        Assert.Equal(2, anywhere.Count);
        Assert.Equal(2, wholeCity.Count);
    }

    [Fact]
    public async Task A_suspended_business_disappears_from_customers_and_returns_when_reinstated()
    {
        var applied = (await _b.Businesses.ApplyAsync(_owner, BusinessHarness.Input("Spice Route", BusinessType.Restaurant, "12345678901234"), None)).Value!;
        var approved = (await _b.Businesses.ApproveAsync(applied.Id, applied.Version, _b.Administrator, None)).Value!;
        await OpenForOrdersAsync(approved.Id, "whiteTown");

        var suspended = (await _b.Businesses.SuspendAsync(approved.Id, approved.Version, "Hygiene complaint", _b.Administrator, None)).Value!;
        var hidden = await _restaurants.ListPublicAsync(null, None);
        await _b.Businesses.ReinstateAsync(suspended.Id, suspended.Version, _b.Administrator, None);
        var back = await _restaurants.ListPublicAsync(null, None);

        Assert.Empty(hidden);
        Assert.Single(back);
    }

    [Fact]
    public async Task The_public_menu_hides_archived_dishes_and_empty_categories_but_greys_out_sold_out_ones()
    {
        var id = await RestaurantBusinessAsync();
        var (category, soldOut) = await DishAsync(id, "Sold out dish");
        var gone = (await _menu.CreateItemAsync(id, _owner, Dish(category, "Gone dish"), None)).Value!;
        var emptyCategory = await CategoryAsync(id, "Empty");
        await _menu.CreateItemAsync(id, _owner, Dish(category, "Still here"), None);
        await _menu.SetAvailabilityAsync(id, soldOut.Id, soldOut.Version, false, _owner, None);
        await _menu.ArchiveItemAsync(id, gone.Id, gone.Version, _owner, None);
        await _restaurants.SaveMineAsync(id, _owner, Open(FoodAreas.WholeCity), None);

        var menu = (await _menu.GetPublicAsync(id, None)).Value!;

        var shown = Assert.Single(menu.Categories);
        Assert.NotEqual(emptyCategory, shown.Id);
        Assert.Equal(["Sold out dish", "Still here"], shown.Items.OrderBy(i => i.Name).Select(i => i.Name).Order());
        Assert.False(shown.Items.Single(i => i.Name == "Sold out dish").IsAvailable);
        Assert.DoesNotContain(shown.Items, i => i.IsArchived);
    }

    // --- Areas ---

    [Fact]
    public void An_area_is_served_when_listed_or_when_the_restaurant_covers_the_whole_city()
    {
        Assert.True(FoodAreas.Serves(["whiteTown"], "whiteTown"));
        Assert.False(FoodAreas.Serves(["whiteTown"], "auroville"));
        Assert.True(FoodAreas.Serves([FoodAreas.WholeCity], "auroville"));
        Assert.True(FoodAreas.Serves(["whiteTown"], null));
        Assert.True(FoodAreas.Serves(["whiteTown"], FoodAreas.WholeCity));
        Assert.False(FoodAreas.Serves([], null));
    }

    [Fact]
    public void The_areas_match_the_ones_in_the_customer_app()
    {
        // The app's area picker (serviceAreas.ts) uses these same codes.
        Assert.Equal(
            ["puducherry", "whiteTown", "tamilQuarter", "auroville", "lawspet", "muthialpet", "reddiarpalayam", "ariyankuppam", "villianur"],
            FoodAreas.All);
    }

    // --- Audit ---

    [Fact]
    public async Task Setting_up_and_menu_changes_are_audited_but_the_sold_out_switch_is_not()
    {
        var id = await RestaurantBusinessAsync();
        var (_, item) = await DishAsync(id);
        await _restaurants.SaveMineAsync(id, _owner, Open("whiteTown"), None);
        await _menu.SetAvailabilityAsync(id, item.Id, item.Version, false, _owner, None);

        var actions = _b.Db.AuditEntries.Select(e => e.Action).ToList();

        Assert.Contains("restaurant.updated", actions);
        Assert.Contains("menu_category.created", actions);
        Assert.Contains("menu_item.created", actions);
        Assert.DoesNotContain(actions, a => a.Contains("availab", StringComparison.Ordinal));
    }
}
