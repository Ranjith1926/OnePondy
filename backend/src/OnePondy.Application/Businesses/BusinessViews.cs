using OnePondy.Domain.Businesses;

namespace OnePondy.Application.Businesses;

public sealed record BusinessDocumentView(Guid Id, BusinessDocumentKind Kind, string ContentType, DateTimeOffset UploadedAt);

public sealed record BusinessSummary(
    Guid Id,
    string Name,
    BusinessType BusinessType,
    BusinessApprovalStatus ApprovalStatus,
    bool IsActive,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record BusinessDetail(
    Guid Id,
    uint Version,
    Guid OwnerUserId,
    string Name,
    BusinessType BusinessType,
    string? Description,
    string? PhoneNumber,
    string? Email,
    string? Address,
    double? Latitude,
    double? Longitude,
    string? Gstin,
    string? FssaiLicenseNumber,
    BusinessApprovalStatus ApprovalStatus,
    bool IsActive,
    string? ReviewNote,
    IReadOnlyList<BusinessDocumentView> Documents,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

/// <summary>Already-parsed input for applying or editing a business (the API layer parses <see cref="BusinessType"/> from its camelCase string).</summary>
public sealed record BusinessApplicationInput(
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
