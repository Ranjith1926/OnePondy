namespace OnePondy.Domain.Businesses;

public enum BusinessType
{
    Restaurant = 1,
    Hotel = 2,
    Activity = 3,
    Shop = 4,
    TourOperator = 5,
    Service = 6,
}

public enum BusinessApprovalStatus
{
    Pending = 1,
    Approved = 2,
    Rejected = 3,
    Suspended = 4,
}

public enum BusinessDocumentKind
{
    Gstin = 1,
    FssaiLicence = 2,
    Other = 3,
}

/// <param name="Latitude">Must be given with <paramref name="Longitude"/>, or neither.</param>
public sealed record BusinessContent(
    string? Name,
    BusinessType BusinessType,
    string? Description,
    string? PhoneNumber,
    string? Email,
    string? Address,
    double? Latitude,
    double? Longitude,
    string? Gstin,
    string? FssaiLicenseNumber);

/// <summary>
/// A partner's business and its onboarding review (spec §10, Phase 6): a customer account applies,
/// an administrator approves, rejects or later suspends it. There is no separate partner account
/// type — owning an approved business is what makes a customer a partner (spec §1: "one customer
/// identity").
/// </summary>
public sealed class Business
{
    private readonly List<BusinessDocument> _documents = [];

    private Business()
    {
    }

    public Guid Id { get; private set; }

    public Guid OwnerUserId { get; private set; }

    public string Name { get; private set; } = string.Empty;

    public BusinessType BusinessType { get; private set; }

    public string? Description { get; private set; }

    public string? PhoneNumber { get; private set; }

    public string? Email { get; private set; }

    public string? Address { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public string? Gstin { get; private set; }

    public string? FssaiLicenseNumber { get; private set; }

    public BusinessApprovalStatus ApprovalStatus { get; private set; }

    /// <summary>Whether the business and its listings may appear publicly; only true while approved.</summary>
    public bool IsActive { get; private set; }

    /// <summary>The reviewer's reason when rejected or suspended.</summary>
    public string? ReviewNote { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin).</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<BusinessDocument> Documents => _documents;

    /// <summary>A pending or rejected application can still be edited; once approved or suspended it cannot.</summary>
    public bool CanEdit => ApprovalStatus is BusinessApprovalStatus.Pending or BusinessApprovalStatus.Rejected;

    public bool CanApprove =>
        ApprovalStatus == BusinessApprovalStatus.Pending
        && (BusinessType != BusinessType.Restaurant || !string.IsNullOrWhiteSpace(FssaiLicenseNumber));

    public static Business Apply(Guid ownerUserId, BusinessContent content, DateTimeOffset now)
    {
        var business = new Business
        {
            Id = Guid.CreateVersion7(),
            OwnerUserId = ownerUserId,
            ApprovalStatus = BusinessApprovalStatus.Pending,
            IsActive = false,
            CreatedAt = now,
        };
        business.UpdateContent(content, now);
        return business;
    }

    /// <summary>Edits the application. A rejected application resubmits itself for review on the next edit.</summary>
    public void UpdateContent(BusinessContent content, DateTimeOffset now)
    {
        EnsureState(CanEdit, "Only a pending or rejected application can be edited.");
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Name);
        if ((content.Latitude is null) != (content.Longitude is null))
        {
            throw new ArgumentException("Provide both latitude and longitude, or neither.", nameof(content));
        }

        Name = content.Name.Trim();
        BusinessType = content.BusinessType;
        Description = content.Description;
        PhoneNumber = content.PhoneNumber;
        Email = content.Email;
        Address = content.Address;
        Latitude = content.Latitude;
        Longitude = content.Longitude;
        Gstin = content.Gstin;
        FssaiLicenseNumber = content.FssaiLicenseNumber;

        if (ApprovalStatus == BusinessApprovalStatus.Rejected)
        {
            ApprovalStatus = BusinessApprovalStatus.Pending;
            ReviewNote = null;
        }

        UpdatedAt = now;
    }

    public void Approve(DateTimeOffset now)
    {
        EnsureState(CanApprove, "This application cannot be approved yet.");
        ApprovalStatus = BusinessApprovalStatus.Approved;
        IsActive = true;
        ReviewNote = null;
        UpdatedAt = now;
    }

    public void Reject(string reason, DateTimeOffset now)
    {
        EnsureState(ApprovalStatus == BusinessApprovalStatus.Pending, "Only a pending application can be rejected.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ApprovalStatus = BusinessApprovalStatus.Rejected;
        IsActive = false;
        ReviewNote = reason.Trim();
        UpdatedAt = now;
    }

    public void Suspend(string reason, DateTimeOffset now)
    {
        EnsureState(ApprovalStatus == BusinessApprovalStatus.Approved, "Only an approved business can be suspended.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ApprovalStatus = BusinessApprovalStatus.Suspended;
        IsActive = false;
        ReviewNote = reason.Trim();
        UpdatedAt = now;
    }

    public void Reinstate(DateTimeOffset now)
    {
        EnsureState(ApprovalStatus == BusinessApprovalStatus.Suspended, "Only a suspended business can be reinstated.");
        ApprovalStatus = BusinessApprovalStatus.Approved;
        IsActive = true;
        ReviewNote = null;
        UpdatedAt = now;
    }

    public BusinessDocument AddDocument(BusinessDocumentKind kind, string storageKey, string contentType, DateTimeOffset now)
    {
        var document = new BusinessDocument(Guid.CreateVersion7(), kind, storageKey, contentType, now);
        _documents.Add(document);
        UpdatedAt = now;
        return document;
    }

    public void RemoveDocument(Guid documentId, DateTimeOffset now)
    {
        var document = _documents.Find(d => d.Id == documentId);
        if (document is null)
        {
            return;
        }

        _documents.Remove(document);
        UpdatedAt = now;
    }

    private static void EnsureState(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed class BusinessDocument
{
    private BusinessDocument()
    {
    }

    internal BusinessDocument(Guid id, BusinessDocumentKind kind, string storageKey, string contentType, DateTimeOffset uploadedAt)
    {
        Id = id;
        Kind = kind;
        StorageKey = storageKey;
        ContentType = contentType;
        UploadedAt = uploadedAt;
    }

    public Guid Id { get; private set; }

    public BusinessDocumentKind Kind { get; private set; }

    /// <summary>Path within the private document store (never the public media folder).</summary>
    public string StorageKey { get; private set; } = string.Empty;

    public string ContentType { get; private set; } = string.Empty;

    public DateTimeOffset UploadedAt { get; private set; }
}
