namespace OnePondy.Domain.Delivery;

public enum DriverApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Suspended = 4,
}

public enum VehicleType
{
    Bicycle = 1,
    Scooter = 2,
    Motorbike = 3,
}

/// <summary>What a driver tells OnePondy about themselves and their vehicle. A bicycle needs no registration or licence.</summary>
public sealed record DriverContent(string FullName, VehicleType VehicleType, string? VehicleNumber, string? LicenceNumber);

/// <summary>
/// A delivery driver (spec Phase 8, step 8.6). An ordinary account applies, an administrator approves, rejects or later suspends it
/// (the same review as a business has); there is no separate driver account type. Only an approved driver can go on duty, and only a
/// driver who is on duty is offered jobs (step 8.7), so an unreviewed person or vehicle never receives one.
/// </summary>
public sealed class DriverProfile
{
    public const int MinNameLength = 2;
    public const int MaxNameLength = 100;
    public const int MaxVehicleNumberLength = 12;
    public const int MaxLicenceLength = 20;
    public const int MaxReasonLength = 1000;

    private DriverProfile()
    {
    }

    /// <summary>The driver's own account: one profile per account.</summary>
    public Guid UserId { get; private set; }

    public string FullName { get; private set; } = string.Empty;

    public VehicleType VehicleType { get; private set; }

    /// <summary>Registration plate, upper case without spaces. Null for a bicycle.</summary>
    public string? VehicleNumber { get; private set; }

    /// <summary>Driving licence number, upper case without spaces. Null for a bicycle.</summary>
    public string? LicenceNumber { get; private set; }

    public DriverApprovalStatus ApprovalStatus { get; private set; }

    /// <summary>The reviewer's reason when rejected or suspended.</summary>
    public string? ReviewNote { get; private set; }

    public bool IsOnDuty { get; private set; }

    public DateTimeOffset? OnDutySince { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    /// <summary>Whether this vehicle needs a registration and a licence on file.</summary>
    public static bool NeedsDocuments(VehicleType type) => type != VehicleType.Bicycle;

    /// <summary>Only an approved driver can go on duty.</summary>
    public bool CanGoOnDuty => ApprovalStatus == DriverApprovalStatus.Approved;

    /// <summary>Whether jobs may be offered to this driver right now: approved and on duty (the rule step 8.7 dispatches by).</summary>
    public bool IsAvailableForJobs => ApprovalStatus == DriverApprovalStatus.Approved && IsOnDuty;

    /// <summary>A suspended driver cannot change anything: support deals with them.</summary>
    public bool CanEdit => ApprovalStatus != DriverApprovalStatus.Suspended;

    public static DriverProfile Apply(Guid userId, DriverContent content, DateTimeOffset now)
    {
        var profile = new DriverProfile
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
    /// Changes the details. An approved driver who changes anything goes back to review and off duty, so the person or vehicle that was
    /// checked is the one that gets jobs; a rejected application resubmits itself. Saving what is already there changes nothing.
    /// </summary>
    public void Edit(DriverContent content, DateTimeOffset now)
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
    public void GoOnDuty(DateTimeOffset now)
    {
        EnsureState(CanGoOnDuty, "Only an approved driver can go on duty.");
        if (IsOnDuty)
        {
            return;
        }

        IsOnDuty = true;
        OnDutySince = now;
        UpdatedAt = now;
    }

    /// <summary>Ends a shift. Always allowed, whatever the driver's status.</summary>
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

    private bool Matches(DriverContent content) =>
        FullName == content.FullName.Trim()
        && VehicleType == content.VehicleType
        && VehicleNumber == Normalised(content.VehicleType, content.VehicleNumber)
        && LicenceNumber == Normalised(content.VehicleType, content.LicenceNumber);

    private void Set(DriverContent content)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content.FullName);
        if (NeedsDocuments(content.VehicleType))
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(content.VehicleNumber);
            ArgumentException.ThrowIfNullOrWhiteSpace(content.LicenceNumber);
        }

        FullName = content.FullName.Trim();
        VehicleType = content.VehicleType;
        VehicleNumber = Normalised(content.VehicleType, content.VehicleNumber);
        LicenceNumber = Normalised(content.VehicleType, content.LicenceNumber);
    }

    /// <summary>Upper case without spaces, hyphens or slashes, so the same plate or licence typed two ways is the same. A bicycle has none.</summary>
    private static string? Normalised(VehicleType type, string? value) =>
        NeedsDocuments(type) && !string.IsNullOrWhiteSpace(value) ? Normalise(value) : null;

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
