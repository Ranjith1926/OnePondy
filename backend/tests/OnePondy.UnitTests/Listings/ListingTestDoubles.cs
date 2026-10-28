using Microsoft.Extensions.Time.Testing;
using OnePondy.Application.Audit;
using OnePondy.Application.Businesses;
using OnePondy.Application.Listings;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Listings;
using OnePondy.UnitTests.Businesses;
using OnePondy.UnitTests.Identity;

namespace OnePondy.UnitTests.Listings;

/// <summary>The listing service wired to in-memory doubles, with an already-approved, active business.</summary>
public sealed class ListingHarness
{
    public ListingHarness()
    {
        Accounts = new FakeUserAccountStore(Time);
        Audit = new AuditTrail(Db, Time);
        var businesses = new BusinessService(Db, new InMemoryBusinessDocumentStorage(), Audit, Time);
        Listings = new ListingService(Db, Audit, Time);

        Owner = Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919876543210", CancellationToken.None).Result.Id;
        Administrator = Accounts.FindOrCreateAsync(OtpChannel.Email, "admin@onepondy.test", CancellationToken.None).Result.Id;
        Accounts.SetStaffRole(Administrator, RoleNames.Administrator);

        var applied = businesses.ApplyAsync(Owner, BusinessHarness.Input(), default).Result;
        var approved = businesses.ApproveAsync(applied.Value!.Id, applied.Value.Version, Administrator, default).Result;
        BusinessId = approved.Value!.Id;
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 28, 4, 0, 0, TimeSpan.Zero));

    public TestDbContext Db { get; } = TestDbContext.Create();

    public FakeUserAccountStore Accounts { get; }

    public AuditTrail Audit { get; }

    public ListingService Listings { get; }

    public Guid Owner { get; }

    public Guid Administrator { get; }

    public Guid BusinessId { get; }

    public static ListingApplicationInput Input(
        ListingType listingType = ListingType.Activity,
        bool isBookable = true,
        decimal? basePrice = 500m) =>
        new(listingType, "Backwater Kayaking", "A two-hour guided kayak tour.", isBookable, basePrice, isBookable ? 24 : null, isBookable ? 50 : null, null);

    /// <summary>Applies, submits and publishes a listing, so a test can go straight to booking it.</summary>
    public async Task<ListingDetail> PublishedListingAsync(ListingApplicationInput? input = null)
    {
        var created = await Listings.CreateAsync(BusinessId, Owner, input ?? Input(), default);
        var submitted = await Listings.SubmitAsync(created.Value!.Id, created.Value.Version, Owner, default);
        var published = await Listings.PublishAsync(submitted.Value!.Id, submitted.Value.Version, Administrator, default);
        return published.Value!;
    }
}
