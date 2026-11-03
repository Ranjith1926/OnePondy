using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using OnePondy.Application.Rides;
using OnePondy.Domain.Configuration;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Rides;
using OnePondy.UnitTests.Businesses;

namespace OnePondy.UnitTests.Rides;

/// <summary>Remembers who was told that a ride changed, so tests can check the right people (and only they) were.</summary>
public sealed class RecordingRideNotifier : IRideNotifier
{
    public List<(Guid RideId, Guid Rider, Guid? Driver)> Changes { get; } = [];

    public Task RideChangedAsync(Guid rideId, Guid riderUserId, Guid? driverUserId, CancellationToken cancellationToken)
    {
        Changes.Add((rideId, riderUserId, driverUserId));
        return Task.CompletedTask;
    }
}

/// <summary>Everything a ride test needs: a rider, drivers, the services, and the places on the map the tests use.</summary>
public sealed class RideHarness
{
    private static readonly CancellationToken None = CancellationToken.None;

    public const double PromenadeLatitude = 11.9340;
    public const double PromenadeLongitude = 79.8350;

    /// <summary>About 9 km from the Promenade.</summary>
    public const double AurovilleLatitude = 12.0053;
    public const double AurovilleLongitude = 79.8100;

    public RideHarness(bool ridesOn = true)
    {
        B = new BusinessHarness();
        Options = new RideOptions();
        Notifier = new RecordingRideNotifier();
        Drivers = new RideDriverService(B.Db, B.Accounts, B.Audit, Microsoft.Extensions.Options.Options.Create(Options), B.Time);
        Rides = new RideService(B.Db, B.Accounts, Notifier, Microsoft.Extensions.Options.Options.Create(Options), B.Time, NullLogger<RideService>.Instance);
        Expiry = new RideExpiry(B.Db, Notifier, B.Time, NullLogger<RideExpiry>.Instance);
        Safety = new RideSafetyService(B.Db, B.Accounts, Hasher, B.Audit, Microsoft.Extensions.Options.Options.Create(Options), B.Time);

        Rider = B.Customer;
        B.Accounts.UpdateProfileAsync(Rider, "Anita Raman", null, None).Wait();
        if (ridesOn)
        {
            B.Db.ModuleFlags.Add(new ModuleFlag(ModuleKeys.Rides, ModuleStatus.Available));
            B.Db.SaveChanges();
        }
    }

    public BusinessHarness B { get; }

    public RideOptions Options { get; }

    public RecordingRideNotifier Notifier { get; }

    public RideDriverService Drivers { get; }

    public RideService Rides { get; }

    public RideExpiry Expiry { get; }

    public RideSafetyService Safety { get; }

    public OnePondy.Application.Common.ISecretHasher Hasher { get; } = new OnePondy.Infrastructure.Security.HmacSecretHasher(
        Microsoft.Extensions.Options.Options.Create(new OnePondy.Infrastructure.Security.SecretHashingOptions { HashingKey = OnePondy.UnitTests.Identity.TestKeys.Hashing }));

    public Guid Rider { get; }

    public DateOnly InsuredUntil => RideClock.Today(B.Time.GetUtcNow()).AddMonths(6);

    public RideDriverInput Application(
        RideVehicleClass vehicleClass = RideVehicleClass.Auto,
        string plate = "PY 01 AB 1234",
        string licence = "PY-0120200012345",
        DateOnly? insuredUntil = null,
        uint? version = null) =>
        new("Ravi Kumar", vehicleClass, plate, licence, "PERMIT-0001", "POLICY-778899", insuredUntil ?? InsuredUntil, version);

    public async Task<Guid> NewAccountAsync(string phone) =>
        (await B.Accounts.FindOrCreateAsync(OtpChannel.Sms, phone, None)).Id;

    /// <summary>An approved, insured driver who is on duty with a fresh position at the given point (the Promenade by default).</summary>
    public async Task<Guid> OnDutyDriverAsync(
        string phone,
        RideVehicleClass vehicleClass = RideVehicleClass.Auto,
        string plate = "PY 01 AB 1234",
        string licence = "PY-0120200012345",
        double latitude = PromenadeLatitude,
        double longitude = PromenadeLongitude)
    {
        var user = await NewAccountAsync(phone);
        var applied = await Drivers.SaveMineAsync(user, Application(vehicleClass, plate, licence), None);
        Assert.True(applied.IsSuccess, applied.Error?.Detail);
        var approved = await Drivers.ApproveAsync(user, applied.Value!.Version, B.Administrator, None);
        Assert.True(approved.IsSuccess, approved.Error?.Detail);
        var version = (await Drivers.GetMineAsync(user, None)).Version;
        var duty = await Drivers.SetDutyAsync(user, true, version, None);
        Assert.True(duty.IsSuccess, duty.Error?.Detail);
        var located = await Drivers.ShareLocationAsync(user, latitude, longitude, None);
        Assert.True(located.IsSuccess, located.Error?.Detail);
        return user;
    }

    public RideQuoteInput Trip(string pickup = "Promenade Beach", string destination = "Auroville") =>
        new(PromenadeLatitude, PromenadeLongitude, pickup, AurovilleLatitude, AurovilleLongitude, destination);

    /// <summary>Asks for fares and returns the quote for the given class.</summary>
    public async Task<RideQuoteView> QuotedAsync(RideVehicleClass vehicleClass = RideVehicleClass.Auto, Guid? rider = null)
    {
        var quotes = await Rides.QuoteAsync(rider ?? Rider, Trip(), None);
        Assert.True(quotes.IsSuccess, quotes.Error?.Detail);
        return quotes.Value!.Options.Single(o => o.VehicleClass == vehicleClass);
    }

    /// <summary>Requests a ride with a fresh quote.</summary>
    public async Task<RideView> RequestedAsync(RideVehicleClass vehicleClass = RideVehicleClass.Auto, Guid? rider = null, string? note = null)
    {
        var quote = await QuotedAsync(vehicleClass, rider);
        var ride = await Rides.RequestAsync(rider ?? Rider, new RequestRideInput(quote.QuoteId, note), None);
        Assert.True(ride.IsSuccess, ride.Error?.Detail);
        return ride.Value!;
    }

    public Task<RideDriverProfile> ProfileAsync(Guid user) => B.Db.RideDriverProfiles.AsNoTracking().SingleAsync(p => p.UserId == user);

    public Task<Ride> RideRowAsync(Guid rideId) => B.Db.Rides.AsNoTracking().SingleAsync(r => r.Id == rideId);
}
