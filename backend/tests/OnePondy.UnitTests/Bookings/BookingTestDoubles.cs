using Microsoft.Extensions.Time.Testing;
using OnePondy.Application.Audit;
using OnePondy.Application.Bookings;
using OnePondy.Application.Businesses;
using OnePondy.Application.Listings;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Listings;
using OnePondy.UnitTests.Businesses;
using OnePondy.UnitTests.Identity;

namespace OnePondy.UnitTests.Bookings;

/// <summary>The booking service wired to in-memory doubles, with a published, bookable listing and one open slot.</summary>
public sealed class BookingHarness
{
    public BookingHarness()
    {
        Accounts = new FakeUserAccountStore(Time);
        var audit = new AuditTrail(Db, Time);
        var businesses = new BusinessService(Db, new InMemoryBusinessDocumentStorage(), audit, Time);
        Listings = new ListingService(Db, audit, Time);
        Bookings = new BookingService(Db, Time, Microsoft.Extensions.Options.Options.Create(Payments));
        PaymentService = new OnePondy.Application.Payments.PaymentService(Db, Gateway, Time, Microsoft.Extensions.Options.Options.Create(Payments), Microsoft.Extensions.Logging.Abstractions.NullLogger<OnePondy.Application.Payments.PaymentService>.Instance);
        Expiry = new BookingHoldExpiry(Db, Bookings, PaymentService, Time, Microsoft.Extensions.Options.Options.Create(Payments));

        Owner = Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919876543210", CancellationToken.None).Result.Id;
        Customer = Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919988776655", CancellationToken.None).Result.Id;
        var administrator = Accounts.FindOrCreateAsync(OtpChannel.Email, "admin@onepondy.test", CancellationToken.None).Result.Id;
        Administrator = administrator;
        Accounts.SetStaffRole(administrator, RoleNames.Administrator);

        var applied = businesses.ApplyAsync(Owner, BusinessHarness.Input(), default).Result;
        var approved = businesses.ApproveAsync(applied.Value!.Id, applied.Value.Version, administrator, default).Result;
        BusinessId = approved.Value!.Id;

        var input = new ListingApplicationInput(ListingType.Activity, "Backwater Kayaking", "A two-hour guided kayak tour.", true, 500m, 24, 50, null);
        var created = Listings.CreateAsync(BusinessId, Owner, input, default).Result;
        var submitted = Listings.SubmitAsync(created.Value!.Id, created.Value.Version, Owner, default).Result;
        ListingId = Listings.PublishAsync(submitted.Value!.Id, submitted.Value.Version, administrator, default).Result.Value!.Id;

        var start = Time.GetUtcNow().AddDays(1);
        SlotId = Listings.CreateSlotAsync(ListingId, Owner, start, start.AddHours(2), 4, default).Result.Value!.Id;
    }

    public OnePondy.Application.Payments.PaymentsOptions Payments { get; } = new();

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 10, 28, 4, 0, 0, TimeSpan.Zero));

    public TestDbContext Db { get; } = TestDbContext.Create();

    public FakeUserAccountStore Accounts { get; }

    public ListingService Listings { get; }

    public BookingService Bookings { get; }

    public FakePaymentGateway Gateway { get; } = new();

    public OnePondy.Application.Payments.PaymentService PaymentService { get; }

    /// <summary>The Worker hold-expiry step, wired to the fake gateway.</summary>
    public BookingHoldExpiry Expiry { get; }

    public Guid Administrator { get; }

    public Guid Owner { get; }

    public Guid Customer { get; }

    public Guid BusinessId { get; }

    public Guid ListingId { get; }

    public Guid SlotId { get; }

    public CreateBookingInput Input(int guestCount = 2, string idempotencyKey = "idem-1") =>
        new(ListingId, [SlotId], guestCount, idempotencyKey);

    /// <summary>Switches online payment on, the way an administrator does in the console.</summary>
    public async Task EnablePaymentsAsync()
    {
        Db.ModuleFlags.Add(new ModuleFlag(ModuleKeys.Payments, ModuleStatus.Available));
        await Db.SaveChangesAsync();
    }

    /// <summary>Publishes a further bookable listing at the given price, with one open slot, and returns a booking request for it.</summary>
    public async Task<CreateBookingInput> NewListingInputAsync(decimal price, string idempotencyKey)
    {
        var input = new ListingApplicationInput(ListingType.Activity, $"Listing at {price}", null, true, price, 24, 50, null);
        var created = (await Listings.CreateAsync(BusinessId, Owner, input, default)).Value!;
        var submitted = (await Listings.SubmitAsync(created.Id, created.Version, Owner, default)).Value!;
        var published = (await Listings.PublishAsync(submitted.Id, submitted.Version, Administrator, default)).Value!;
        var start = Time.GetUtcNow().AddDays(2);
        var slot = (await Listings.CreateSlotAsync(published.Id, Owner, start, start.AddHours(1), 4, default)).Value!;
        return new CreateBookingInput(published.Id, [slot.Id], 2, idempotencyKey);
    }
}
