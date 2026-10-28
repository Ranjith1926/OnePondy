using Microsoft.EntityFrameworkCore;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Businesses;

namespace OnePondy.Application.Businesses;

public sealed record BusinessListQuery(BusinessApprovalStatus? Status, int? Page, int? PageSize);

/// <summary>
/// Partner onboarding (spec §10, Phase 6): a customer applies, an administrator approves, rejects or
/// later suspends it. Owning an approved business is what makes a customer a partner — there is no
/// separate partner role or account type (spec §1).
/// </summary>
public sealed class BusinessService(IOnePondyDbContext db, IBusinessDocumentStorage storage, AuditTrail audit, TimeProvider time)
{
    public const long MaxDocumentBytes = 10 * 1024 * 1024;
    public const int MaxReasonLength = 1000;

    public async Task<IReadOnlyList<BusinessSummary>> ListMineAsync(Guid ownerUserId, CancellationToken cancellationToken)
    {
        var businesses = await db.Businesses
            .AsNoTracking()
            .Where(b => b.OwnerUserId == ownerUserId)
            .OrderByDescending(b => b.CreatedAt)
            .ToListAsync(cancellationToken);
        return [.. businesses.Select(ToSummary)];
    }

    public async Task<Result<BusinessDetail>> GetMineAsync(Guid id, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var business = await db.Businesses.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        return business is null || business.OwnerUserId != ownerUserId ? BusinessErrors.NotFound : ToDetail(business);
    }

    public async Task<Result<BusinessDetail>> ApplyAsync(Guid ownerUserId, BusinessApplicationInput input, CancellationToken cancellationToken)
    {
        var content = BusinessInputValidator.Validate(input);
        if (!content.IsSuccess)
        {
            return content.Error;
        }

        var business = Business.Apply(ownerUserId, content.Value, time.GetUtcNow());
        db.Businesses.Add(business);
        audit.Record(ownerUserId, AuditActions.BusinessApplied, AuditEntityTypes.Business, business.Id, new { business.Name, business.BusinessType });
        await db.SaveChangesAsync(cancellationToken);
        return ToDetail(business);
    }

    public async Task<Result<BusinessDetail>> UpdateAsync(
        Guid id,
        uint expectedVersion,
        Guid ownerUserId,
        BusinessApplicationInput input,
        CancellationToken cancellationToken)
    {
        var content = BusinessInputValidator.Validate(input);
        if (!content.IsSuccess)
        {
            return content.Error;
        }

        var business = await db.Businesses.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (business is null || business.OwnerUserId != ownerUserId)
        {
            return BusinessErrors.NotFound;
        }

        if (!business.CanEdit)
        {
            return BusinessErrors.CannotEdit;
        }

        db.ExpectVersion(business, expectedVersion);
        business.UpdateContent(content.Value, time.GetUtcNow());
        audit.Record(ownerUserId, AuditActions.BusinessUpdated, AuditEntityTypes.Business, business.Id);
        return await SaveAsync(business, cancellationToken);
    }

    public async Task<Result<BusinessDocumentView>> AddDocumentAsync(
        Guid businessId,
        Guid ownerUserId,
        BusinessDocumentKind kind,
        ReadOnlyMemory<byte> content,
        string contentType,
        CancellationToken cancellationToken)
    {
        var business = await db.Businesses.SingleOrDefaultAsync(b => b.Id == businessId, cancellationToken);
        if (business is null || business.OwnerUserId != ownerUserId)
        {
            return BusinessErrors.NotFound;
        }

        if (!business.CanEdit)
        {
            return BusinessErrors.CannotEdit;
        }

        var storageKey = $"{business.Id}/{Guid.CreateVersion7()}";
        await storage.SaveAsync(storageKey, content, cancellationToken);
        var document = business.AddDocument(kind, storageKey, contentType, time.GetUtcNow());

        // A new entry added directly to an owned collection on an already-tracked, Unchanged parent
        // is picked up by change detection, but as Modified rather than Added — there is nothing in
        // its own history to diff against yet. Told explicitly, it inserts correctly either way.
        db.MarkAdded(document);

        audit.Record(ownerUserId, AuditActions.BusinessDocumentAdded, AuditEntityTypes.Business, business.Id, new { kind });
        await db.SaveChangesAsync(cancellationToken);
        return ToDocumentView(document);
    }

    public async Task<AppError?> RemoveDocumentAsync(Guid businessId, Guid documentId, Guid ownerUserId, CancellationToken cancellationToken)
    {
        var business = await db.Businesses.SingleOrDefaultAsync(b => b.Id == businessId, cancellationToken);
        if (business is null || business.OwnerUserId != ownerUserId)
        {
            return BusinessErrors.NotFound;
        }

        var document = business.Documents.SingleOrDefault(d => d.Id == documentId);
        if (document is null)
        {
            return BusinessErrors.DocumentNotFound;
        }

        business.RemoveDocument(documentId, time.GetUtcNow());
        await storage.DeleteAsync(document.StorageKey, cancellationToken);
        audit.Record(ownerUserId, AuditActions.BusinessDocumentRemoved, AuditEntityTypes.Business, business.Id, new { document.Kind });
        await db.SaveChangesAsync(cancellationToken);
        return null;
    }

    /// <param name="requireOwnerUserId">Null for a staff request; otherwise the document must belong to this owner.</param>
    public async Task<Result<(byte[] Content, string ContentType)>> GetDocumentAsync(
        Guid businessId,
        Guid documentId,
        Guid? requireOwnerUserId,
        CancellationToken cancellationToken)
    {
        var business = await db.Businesses.AsNoTracking().SingleOrDefaultAsync(b => b.Id == businessId, cancellationToken);
        if (business is null)
        {
            return BusinessErrors.NotFound;
        }

        if (requireOwnerUserId is { } ownerUserId && business.OwnerUserId != ownerUserId)
        {
            return BusinessErrors.NotFound;
        }

        var document = business.Documents.SingleOrDefault(d => d.Id == documentId);
        if (document is null)
        {
            return BusinessErrors.DocumentNotFound;
        }

        var bytes = await storage.ReadAsync(document.StorageKey, cancellationToken);
        return bytes is null ? BusinessErrors.DocumentNotFound : (bytes, document.ContentType);
    }

    public async Task<PagedResult<BusinessSummary>> AdminListAsync(BusinessListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var businesses = db.Businesses.AsNoTracking();
        if (query.Status is { } status)
        {
            businesses = businesses.Where(b => b.ApprovalStatus == status);
        }

        var total = await businesses.CountAsync(cancellationToken);
        var items = await businesses
            .OrderByDescending(b => b.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);
        return new PagedResult<BusinessSummary>([.. items.Select(ToSummary)], page, pageSize, total);
    }

    public async Task<Result<BusinessDetail>> AdminGetAsync(Guid id, CancellationToken cancellationToken)
    {
        var business = await db.Businesses.AsNoTracking().SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        return business is null ? BusinessErrors.NotFound : ToDetail(business);
    }

    public Task<Result<BusinessDetail>> ApproveAsync(Guid id, uint version, Guid staffUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, version, staffUserId, AuditActions.BusinessApproved, null, business =>
        {
            if (!business.CanApprove)
            {
                return BusinessErrors.CannotApprove;
            }

            business.Approve(time.GetUtcNow());
            return null;
        }, cancellationToken);

    public Task<Result<BusinessDetail>> RejectAsync(Guid id, uint version, string? reason, Guid staffUserId, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("reason", reason, MaxReasonLength);
        return errors.Any
            ? Task.FromResult(Result<BusinessDetail>.Failure(errors.ToError()))
            : TransitionAsync(id, version, staffUserId, AuditActions.BusinessRejected, new { reason = trimmed }, business =>
            {
                if (business.ApprovalStatus != BusinessApprovalStatus.Pending)
                {
                    return BusinessErrors.InvalidTransition("Only a pending application can be rejected.");
                }

                business.Reject(trimmed, time.GetUtcNow());
                return null;
            }, cancellationToken);
    }

    public Task<Result<BusinessDetail>> SuspendAsync(Guid id, uint version, string? reason, Guid staffUserId, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("reason", reason, MaxReasonLength);
        return errors.Any
            ? Task.FromResult(Result<BusinessDetail>.Failure(errors.ToError()))
            : TransitionAsync(id, version, staffUserId, AuditActions.BusinessSuspended, new { reason = trimmed }, business =>
            {
                if (business.ApprovalStatus != BusinessApprovalStatus.Approved)
                {
                    return BusinessErrors.InvalidTransition("Only an approved business can be suspended.");
                }

                business.Suspend(trimmed, time.GetUtcNow());
                return null;
            }, cancellationToken);
    }

    public Task<Result<BusinessDetail>> ReinstateAsync(Guid id, uint version, Guid staffUserId, CancellationToken cancellationToken) =>
        TransitionAsync(id, version, staffUserId, AuditActions.BusinessReinstated, null, business =>
        {
            if (business.ApprovalStatus != BusinessApprovalStatus.Suspended)
            {
                return BusinessErrors.InvalidTransition("Only a suspended business can be reinstated.");
            }

            business.Reinstate(time.GetUtcNow());
            return null;
        }, cancellationToken);

    private async Task<Result<BusinessDetail>> TransitionAsync(
        Guid id,
        uint version,
        Guid userId,
        string auditAction,
        object? auditDetails,
        Func<Business, AppError?> apply,
        CancellationToken cancellationToken)
    {
        var business = await db.Businesses.SingleOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (business is null)
        {
            return BusinessErrors.NotFound;
        }

        // The reviewer acts on the version they saw, never on content changed since.
        if (business.Version != version)
        {
            return BusinessErrors.Changed;
        }

        db.ExpectVersion(business, version);
        if (apply(business) is { } error)
        {
            return error;
        }

        audit.Record(userId, auditAction, AuditEntityTypes.Business, business.Id, auditDetails);
        return await SaveAsync(business, cancellationToken);
    }

    private async Task<Result<BusinessDetail>> SaveAsync(Business business, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return BusinessErrors.Changed;
        }

        return ToDetail(business);
    }

    private static BusinessSummary ToSummary(Business business) => new(
        business.Id,
        business.Name,
        business.BusinessType,
        business.ApprovalStatus,
        business.IsActive,
        business.CreatedAt,
        business.UpdatedAt);

    private static BusinessDetail ToDetail(Business business) => new(
        business.Id,
        business.Version,
        business.OwnerUserId,
        business.Name,
        business.BusinessType,
        business.Description,
        business.PhoneNumber,
        business.Email,
        business.Address,
        business.Latitude,
        business.Longitude,
        business.Gstin,
        business.FssaiLicenseNumber,
        business.ApprovalStatus,
        business.IsActive,
        business.ReviewNote,
        [.. business.Documents.Select(ToDocumentView)],
        business.CreatedAt,
        business.UpdatedAt);

    private static BusinessDocumentView ToDocumentView(BusinessDocument document) => new(
        document.Id,
        document.Kind,
        document.ContentType,
        document.UploadedAt);
}
