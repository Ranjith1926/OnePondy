using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using OnePondy.Application.Audit;
using OnePondy.Application.Configuration;
using OnePondy.Application.Itineraries;
using OnePondy.Application.Media;
using OnePondy.Application.Places;
using OnePondy.Application.Staff;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Places;
using OnePondy.Infrastructure.Media;
using OnePondy.UnitTests.Identity;

namespace OnePondy.UnitTests.Content;

/// <summary>Keeps staff roles on the fake account store, so sign-in sees the same roles.</summary>
public sealed class FakeStaffDirectory(FakeUserAccountStore accounts) : IStaffDirectory
{
    public Task<IReadOnlyList<StaffMember>> ListAsync(CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyList<StaffMember>>([.. accounts.StaffMembers()]);

    public Task<StaffMember?> FindAsync(Guid userId, CancellationToken cancellationToken) =>
        Task.FromResult(accounts.StaffMembers().SingleOrDefault(m => m.UserId == userId));

    public Task SetRoleAsync(Guid userId, string? role, CancellationToken cancellationToken)
    {
        accounts.SetStaffRole(userId, role);
        return Task.CompletedTask;
    }

    public Task<int> CountWithRoleAsync(string role, CancellationToken cancellationToken) =>
        Task.FromResult(accounts.StaffMembers().Count(m => m.Role == role));

    public Task<IReadOnlyDictionary<Guid, string>> GetNamesAsync(
        IReadOnlyCollection<Guid> userIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, string>>(
            userIds.Distinct().ToDictionary(id => id, id => accounts.NameOf(id) ?? "Deleted user"));
}

public sealed class InMemoryMediaStorage : IMediaStorage
{
    public Dictionary<string, byte[]> Files { get; } = [];

    public Task SaveAsync(string path, ReadOnlyMemory<byte> content, string contentType, CancellationToken cancellationToken)
    {
        Files[path] = content.ToArray();
        return Task.CompletedTask;
    }

    public Task DeleteAsync(string path, CancellationToken cancellationToken)
    {
        Files.Remove(path);
        return Task.CompletedTask;
    }

    public string GetPublicUrl(string path) => $"https://cdn.test/{path}";
}

/// <summary>The content services wired to in-memory doubles, with seeded categories and staff.</summary>
public sealed class ContentHarness
{
    public ContentHarness()
    {
        Accounts = new FakeUserAccountStore(Time);
        Staff = new FakeStaffDirectory(Accounts);
        Audit = new AuditTrail(Db, Time);
        var content = Options.Create(new ContentOptions());
        var media = Options.Create(new MediaOptions());

        Places = new PlaceAdminService(Db, Storage, Staff, Audit, content, Time);
        Catalog = new PlaceCatalogService(Db, Storage);
        Media = new MediaService(Db, Storage, new SkiaImageProcessor(media), Audit, media, Time, NullLogger<MediaService>.Instance);
        Config = new ConfigService(Db, Audit, Time);
        StaffService = new StaffService(Db, Staff, Accounts, Audit, Time);
        SavedPlaces = new SavedPlacesService(Db, Catalog, Time);
        Itineraries = new ItineraryService(Db, Catalog, Time);

        foreach (var (code, index) in new[] { "beach", "heritage", "spiritual", "museum", "nature" }.Select((c, i) => (c, i)))
        {
            Db.PlaceCategories.Add(new PlaceCategory(code, code, index));
        }

        Db.SaveChanges();
        Administrator = AddStaff("admin@onepondy.test", RoleNames.Administrator, "Admin");
        Editor = AddStaff("editor@onepondy.test", RoleNames.ContentEditor, "Asha");
        Customer = Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919876543210", CancellationToken.None).Result.Id;
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 26, 4, 0, 0, TimeSpan.Zero));

    public TestDbContext Db { get; } = TestDbContext.Create();

    public FakeUserAccountStore Accounts { get; }

    public FakeStaffDirectory Staff { get; }

    public InMemoryMediaStorage Storage { get; } = new();

    public AuditTrail Audit { get; }

    public PlaceAdminService Places { get; }

    public PlaceCatalogService Catalog { get; }

    public MediaService Media { get; }

    public ConfigService Config { get; }

    public StaffService StaffService { get; }

    public SavedPlacesService SavedPlaces { get; }

    public ItineraryService Itineraries { get; }

    public Guid Administrator { get; }

    public Guid Editor { get; }

    /// <summary>A plain customer account (not staff), for saved places and itineraries.</summary>
    public Guid Customer { get; }

    public Guid AddStaff(string email, string role, string name = "")
    {
        var account = Accounts.FindOrCreateAsync(OtpChannel.Email, email, CancellationToken.None).Result;
        Accounts.UpdateProfileAsync(account.Id, name, null, CancellationToken.None).Wait();
        Accounts.SetStaffRole(account.Id, role);
        return account.Id;
    }

    public static PlaceInput Input(
        string name = "Paradise Beach",
        string? description = "A quiet beach reached by boat from Chunnambar.",
        double? latitude = 11.8915,
        double? longitude = 79.8257,
        IReadOnlyList<TranslationInput>? translations = null) =>
        new(
            name,
            "beach",
            description,
            "Carry water.",
            "Chunnambar",
            latitude,
            longitude,
            new OpeningHoursInput(false, [new OpeningPeriodInput(DayOfWeek.Monday, "08:00", "17:30")], null),
            [new EntryFeeInput(VisitorCategory.Standard, 50m)],
            translations ?? []);

    /// <summary>Creates a place and takes it through review to published.</summary>
    public async Task<PlaceDetail> PublishedPlaceAsync(PlaceInput? input = null)
    {
        var place = (await Places.CreateAsync(input ?? Input(), Editor, default)).Value!;
        place = (await Places.SubmitAsync(place.Id, place.Version, Editor, default)).Value!;
        place = (await Places.VerifyAsync(place.Id, place.Version, "Site visit", Administrator, default)).Value!;
        return (await Places.PublishAsync(place.Id, place.Version, Administrator, default)).Value!;
    }
}
