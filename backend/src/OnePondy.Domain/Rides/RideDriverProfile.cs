using OnePondy.Domain.Delivery;

namespace OnePondy.Domain.Rides;

public enum RideVehicleClass
{
    Auto = 1,
    Cab = 2,
    BikeTaxi = 3,
}

/// <summary>What a ride driver tells OnePondy about themselves and their vehicle. Every field is required: passenger transport has no exemptions.</summary>
public sealed record RideDriverContent(
    string FullName,
    RideVehicleClass VehicleClass,
    string VehicleNumber,
    string LicenceNumber,
    string PermitNumber,
    string InsurancePolicyNumber,
    DateOnly InsuranceValidUntil);

/// <summary>
/// A driver in ride mode (spec Phase 9). It is a separate approval from delivering food: passenger transport needs a permit and insurance, which
/// an administrator checks. Being eligible for rides means approved <i>and</i> insured on the day (the policy's end date has not passed), so a lapsed
/// policy takes the driver out of service without anyone having to remember. Only an eligible driver goes on duty for rides, and only one who is on
/// duty with a recent position is offered a job.
/// <para>
/// The driver's position is kept for dispatch only (<see cref="RideDriverPosition"/>): the latest one, only while on duty, and erased when the shift
/// ends. It lives in its own row so that reporting it every few seconds never changes this profile's version, which administrators and the driver
/// rely on to know they are acting on what they last saw.
/// </para>
/// </summary>
public sealed class RideDriverProfile
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;
    public const int MaxVehicleNumberLength = 12;
    public const int MaxLicenceLength = 20;
    public const int MaxPermitLength = 30;
    public const int MaxPolicyLength = 30;
    public const int MaxReasonLength = 1000;

    /// <summary>A position older than this is ignored when choosing who to offer a job to.</summary>
    public static readonly TimeSpan PositionFreshFor = TimeSpan.FromMinutes(2);

    private RideDriverProfile()
    {
    }

    /// <summary>The driver's own account: one ride profile per account.</summary>
    public Guid UserId { get; private set; }

    public string FullName { get; private set; } = string.Empty;

    public RideVehicleClass VehicleClass { get; private set; }

    /// <summary>Registration plate, upper case without spaces.</summary>
    public string VehicleNumber { get; private set; } = string.Empty;

    /// <summary>Driving licence number, upper case without spaces.</summary>
    public string LicenceNumber { get; private set; } = string.Empty;

    /// <summary>The permit to carry passengers for hire, upper case without spaces.</summary>
    public string PermitNumber { get; private set; } = string.Empty;

    public string InsurancePolicyNumber { get; private set; } = string.Empty;

    /// <summary>The last day the policy covers.</summary>
    public DateOnly InsuranceValidUntil { get; private set; }

    public DriverApprovalStatus ApprovalStatus { get; private set; }

    /// <summary>The reviewer's reason when rejected or suspended.</summary>
    public string? ReviewNote { get; private set; }

    public bool IsOnDuty { get; private set; }

    public DateTimeOffset? OnDutySince { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    /// <summary>Approved, and insured on the given day. Nothing else makes a driver eligible for rides.</summary>
    public bool IsEligible(DateOnly today) => ApprovalStatus == DriverApprovalStatus.Approved && InsuranceValidUntil >= today;

    /// <summary>Whether a job may be offered to this driver right now: eligible and on duty.</summary>
    public bool IsAvailableForRides(DateOnly today) => IsEligible(today) && IsOnDuty;

    /// <summary>A suspended driver cannot change anything: support deals with them.</summary>
    public bool CanEdit => ApprovalStatus != DriverApprovalStatus.Suspended;

    public static RideDriverProfile Apply(Guid userId, RideDriverContent content, DateTimeOffset now)
    {
        var profile = new RideDriverProfile
        {
            UserId = userId,
            ApprovalStatus = DriverApprovalStatus.Pending,
            CreatedAt = now,
            UpdatedAt = now,
        };
        profile.Set(content);
        return profile;
    }

    /// <summary>
    /// Changes the details. An approved driver who changes anything goes back to review and off duty, so the person, vehicle and policy that were
    /// checked are the ones that get jobs; a rejected application resubmits itself. Saving what is already there changes nothing.
    /// </summary>
    public void Edit(RideDriverContent content, DateTimeOffset now)
    {
        EnsureState(CanEdit, "A suspended driver cannot be edited. Contact support.");
        if (Matches(content))
        {
            return;
        }

        Set(content);
        if (ApprovalStatus is DriverApprovalStatus.Approved or DriverApprovalStatus.Rejected)
        {
            ApprovalStatus = DriverApprovalStatus.Pending;
            ReviewNote = null;
            StopDuty();
        }

        UpdatedAt = now;
    }

    public void Approve(DateTimeOffset now)
    {
        EnsureState(ApprovalStatus == DriverApprovalStatus.Pending, "Only a pending application can be approved.");
        ApprovalStatus = DriverApprovalStatus.Approved;
        ReviewNote = null;
        UpdatedAt = now;
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        EnsureState(ApprovalStatus == DriverApprovalStatus.Pending, "Only a pending application can be rejected.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ApprovalStatus = DriverApprovalStatus.Rejected;
        ReviewNote = reason.Trim();
        UpdatedAt = now;
    }

    /// <summary>Takes an approved driver out of service at once: they are off duty too.</summary>
    public void Suspend(string reason, DateTimeOffset now)
    {
        EnsureState(ApprovalStatus == DriverApprovalStatus.Approved, "Only an approved driver can be suspended.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ApprovalStatus = DriverApprovalStatus.Suspended;
        ReviewNote = reason.Trim();
        StopDuty();
        UpdatedAt = now;
    }

    /// <summary>Back to approved, but off duty: the driver chooses when to start again.</summary>
    public void Reinstate(DateTimeOffset now)
    {
        EnsureState(ApprovalStatus == DriverApprovalStatus.Suspended, "Only a suspended driver can be reinstated.");
        ApprovalStatus = DriverApprovalStatus.Approved;
        ReviewNote = null;
        UpdatedAt = now;
    }

    /// <summary>Starts a shift. Already on duty is fine and keeps the original start time.</summary>
    public void GoOnDuty(DateOnly today, DateTimeOffset now)
    {
        EnsureState(IsEligible(today), "Only an approved driver with insurance that has not ended can go on duty.");
        if (IsOnDuty)
        {
            return;
        }

        IsOnDuty = true;
        OnDutySince = now;
        UpdatedAt = now;
    }

    /// <summary>Ends a shift, and erases the position. Always allowed, whatever the driver's status.</summary>
    public void GoOffDuty(DateTimeOffset now)
    {
        if (!IsOnDuty)
        {
            return;
        }

        StopDuty();
        UpdatedAt = now;
    }

    private void StopDuty()
    {
        IsOnDuty = false;
        OnDutySince = null;
    }

    private bool Matches(RideDriverContent content) =>
        FullName == content.FullName.Trim()
        && VehicleClass == content.VehicleClass
        && VehicleNumber == Normalise(content.VehicleNumber)
        && LicenceNumber == Normalise(content.LicenceNumber)
        && PermitNumber == Normalise(content.PermitNumber)
        && InsurancePolicyNumber == Normalise(content.InsurancePolicyNumber)
        && InsuranceValidUntil == content.InsuranceValidUntil;

    private void Set(RideDriverContent content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content.FullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.VehicleNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.LicenceNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.PermitNumber);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.InsurancePolicyNumber);

        FullName = content.FullName.Trim();
        VehicleClass = content.VehicleClass;
        VehicleNumber = Normalise(content.VehicleNumber);
        LicenceNumber = Normalise(content.LicenceNumber);
        PermitNumber = Normalise(content.PermitNumber);
        InsurancePolicyNumber = Normalise(content.InsurancePolicyNumber);
        InsuranceValidUntil = content.InsuranceValidUntil;
    }

    /// <summary>Upper case without spaces, hyphens or slashes, so the same number typed two ways is the same.</summary>
    public static string Normalise(string value) =>
        new([.. value.Where(c => c is not (' ' or '-' or '/')).Select(char.ToUpperInvariant)]);

    private static void EnsureState(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

/// <summary>
/// Where an on-duty ride driver last said they were, so requests near them can be offered to them. Only the latest is kept, there is never a history,
/// and the database deletes it the moment the driver goes off duty (and refuses to keep one for a driver who is not on duty).
/// </summary>
public sealed class RideDriverPosition
{
    private RideDriverPosition()
    {
    }

    public Guid UserId { get; private set; }

    public double Latitude { get; private set; }

    public double Longitude { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }

    /// <summary>Whether this is recent enough to say where the driver is.</summary>
    public bool IsFresh(DateTimeOffset now) => now - RecordedAt <= RideDriverProfile.PositionFreshFor;

    public static RideDriverPosition At(Guid userId, double latitude, double longitude, DateTimeOffset now)
    {
        var position = new RideDriverPosition { UserId = userId };
        position.MoveTo(latitude, longitude, now);
        return position;
    }

    public void MoveTo(double latitude, double longitude, DateTimeOffset now)
    {
        if (!double.IsFinite(latitude) || !double.IsFinite(longitude) || latitude is < -90 or > 90 || longitude is < -180 or > 180)
        {
            throw new ArgumentOutOfRangeException(nameof(latitude), "That is not a position on Earth.");
        }

        Latitude = latitude;
        Longitude = longitude;
        RecordedAt = now;
    }
}
