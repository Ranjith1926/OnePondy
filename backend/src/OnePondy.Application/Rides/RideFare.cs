using OnePondy.Domain.Rides;

namespace OnePondy.Application.Rides;

/// <summary>One vehicle class's tariff, in rupees. These defaults are placeholders: the owner sets the real ones (phase-9-plan.md §5).</summary>
public sealed class RideTariff
{
    public RideTariff()
    {
    }

    public RideTariff(decimal baseFare, decimal perKm, decimal minimumFare)
    {
        BaseFare = baseFare;
        PerKm = perKm;
        MinimumFare = minimumFare;
    }

    public decimal BaseFare { get; set; }

    public decimal PerKm { get; set; }

    /// <summary>The least a trip costs, however short.</summary>
    public decimal MinimumFare { get; set; }
}

/// <summary>Settings for rides (spec Phase 9). Whether rides are offered at all is the <c>rides</c> module, which stays off until the owner is ready.</summary>
public sealed class RideOptions
{
    public const string SectionName = "Rides";

    /// <summary>The vehicle classes offered. Bike taxis are not lawful everywhere, so they are off unless the owner turns them on.</summary>
    public List<RideVehicleClass> EnabledClasses { get; set; } = [RideVehicleClass.Auto, RideVehicleClass.Cab];

    public Dictionary<RideVehicleClass, RideTariff> Tariffs { get; set; } = new()
    {
        [RideVehicleClass.Auto] = new RideTariff(30m, 15m, 40m),
        [RideVehicleClass.Cab] = new RideTariff(50m, 18m, 80m),
        [RideVehicleClass.BikeTaxi] = new RideTariff(15m, 8m, 25m),
    };

    /// <summary>Road distance is estimated as the straight line times this, because roads are not straight.</summary>
    public decimal RoadFactor { get; set; } = 1.3m;

    /// <summary>How long a fare quote stays valid.</summary>
    public TimeSpan QuoteValidFor { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long a request searches for a driver before it expires.</summary>
    public TimeSpan SearchWindow { get; set; } = TimeSpan.FromMinutes(10);

    /// <summary>A driver is offered a request only when their last position is within this distance of the pickup.</summary>
    public double OfferRadiusKm { get; set; } = 5;

    /// <summary>Trips shorter or longer than this (road estimate) are refused.</summary>
    public double MinTripKm { get; set; } = 0.3;

    public double MaxTripKm { get; set; } = 60;

    /// <summary>Rides run only around this point, within <see cref="ServiceRadiusKm"/>. The default is Puducherry.</summary>
    public double ServiceCentreLatitude { get; set; } = 11.9416;

    public double ServiceCentreLongitude { get; set; } = 79.8083;

    public double ServiceRadiusKm { get; set; } = 40;

    /// <summary>What the rider is told about cancelling, and what is copied onto the ride. No fee is charged in this version.</summary>
    public string CancellationTerms { get; set; } = "You can cancel free of charge until your trip starts.";

    /// <summary>The most open requests shown to a driver at once, oldest first.</summary>
    public int MaxOffers { get; set; } = 20;

    /// <summary>The number the apps tell a person to call in an emergency. The national emergency number; OnePondy is not an emergency service.</summary>
    public string EmergencyNumber { get; set; } = "112";

    /// <summary>The most incident reports one person can have waiting for staff at a time.</summary>
    public int MaxOpenIncidentsPerPerson { get; set; } = 5;
}

/// <summary>Refuses to start with ride settings that would give nonsense fares or searches.</summary>
public static class RideOptionsGuard
{
    public static void EnsureSafe(RideOptions options)
    {
        foreach (var vehicleClass in options.EnabledClasses)
        {
            if (!options.Tariffs.TryGetValue(vehicleClass, out var tariff)
                || tariff.BaseFare is < 0m or > 5000m
                || tariff.PerKm is <= 0m or > 500m
                || tariff.MinimumFare is <= 0m or > 5000m)
            {
                throw new InvalidOperationException($"Rides:Tariffs for {vehicleClass} must have a base fare of 0 to 5000, a per-km rate above 0 and a minimum fare above 0.");
            }
        }

        if (options.RoadFactor is < 1m or > 3m)
        {
            throw new InvalidOperationException("Rides:RoadFactor must be between 1 and 3.");
        }

        if (options.QuoteValidFor < TimeSpan.FromMinutes(1) || options.QuoteValidFor > TimeSpan.FromMinutes(30))
        {
            throw new InvalidOperationException("Rides:QuoteValidFor must be between 1 and 30 minutes.");
        }

        if (options.SearchWindow < TimeSpan.FromMinutes(1) || options.SearchWindow > TimeSpan.FromMinutes(60))
        {
            throw new InvalidOperationException("Rides:SearchWindow must be between 1 and 60 minutes.");
        }

        if (options.OfferRadiusKm is <= 0 or > 100 || options.ServiceRadiusKm is <= 0 or > 500 || options.MinTripKm < 0 || options.MaxTripKm <= options.MinTripKm)
        {
            throw new InvalidOperationException("Rides: the offer radius, service radius and trip length limits are not sensible.");
        }

        if (string.IsNullOrWhiteSpace(options.EmergencyNumber) || options.MaxOpenIncidentsPerPerson < 1)
        {
            throw new InvalidOperationException("Rides:EmergencyNumber must be set and Rides:MaxOpenIncidentsPerPerson must be at least 1.");
        }

        if (string.IsNullOrWhiteSpace(options.CancellationTerms))
        {
            throw new InvalidOperationException("Rides:CancellationTerms must say what a rider is told about cancelling.");
        }
    }
}

/// <summary>The arithmetic of a fare. Pure, so the same trip always costs the same, which is what makes a quote authoritative.</summary>
public static class RideFare
{
    private const double EarthRadiusKm = 6371.0088;

    /// <summary>The straight-line distance between two points, in kilometres (haversine).</summary>
    public static double StraightLineKm(double latitude1, double longitude1, double latitude2, double longitude2)
    {
        static double Radians(double degrees) => degrees * Math.PI / 180;
        var dLat = Radians(latitude2 - latitude1);
        var dLon = Radians(longitude2 - longitude1);
        var a = (Math.Sin(dLat / 2) * Math.Sin(dLat / 2))
            + (Math.Cos(Radians(latitude1)) * Math.Cos(Radians(latitude2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2));
        return 2 * EarthRadiusKm * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    /// <summary>The estimated road distance, to one decimal place and never below 0.1.</summary>
    public static decimal RoadKm(double straightLineKm, decimal roadFactor) =>
        Math.Max(0.1m, Math.Round((decimal)straightLineKm * roadFactor, 1, MidpointRounding.AwayFromZero));

    /// <summary>The base plus the per-kilometre rate, never below the minimum, rounded up to a whole rupee so there is no change to find.</summary>
    public static decimal Fare(RideTariff tariff, decimal roadKm) =>
        Math.Ceiling(Math.Max(tariff.MinimumFare, tariff.BaseFare + (tariff.PerKm * roadKm)));
}
