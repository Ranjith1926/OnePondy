using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Common;
using OnePondy.Application.Media;
using OnePondy.Application.Staff;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Places;

namespace OnePondy.Application.Places;

public sealed record PlaceListQuery(
    VerificationStatus? Status,
    string? CategoryCode,
    string? Search,
    bool? Published,
    bool ReviewDue,
    int? Page,
    int? PageSize);

/// <summary>Console place management and the verification workflow (spec §10, Phase 3).</summary>
public sealed class PlaceAdminService(
    IOnePondyDbContext db,
    IMediaStorage storage,
    IStaffDirectory staff,
    AuditTrail audit,
    IOptions<ContentOptions> options,
    TimeProvider time)
{
    public const int MaxNoteLength = 1000;
    public const int MaxSourceLength = 500;

    private readonly TimeSpan _reviewInterval = options.Value.ReviewInterval;

    public async Task<IReadOnlyList<CategoryView>> CategoriesAsync(CancellationToken cancellationToken) =>
        await db.PlaceCategories
            .Where(c => c.IsActive)
            .OrderBy(c => c.SortOrder)
            .Select(c => new CategoryView(c.Code, c.Name))
            .ToListAsync(cancellationToken);

    public async Task<PagedResult<PlaceSummary>> ListAsync(PlaceListQuery query, CancellationToken cancellationToken)
    {
        var (page, pageSize) = Paging.Normalize(query.Page, query.PageSize);
        var now = time.GetUtcNow();
        var reviewCutoff = now - _reviewInterval;
        var places = db.Places.AsNoTracking();

        if (query.Status is { } status)
        {
            places = places.Where(p => p.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.CategoryCode))
        {
            places = places.Where(p => p.CategoryCode == query.CategoryCode);
        }

        if (query.Published is { } published)
        {
            places = places.Where(p => p.IsPublished == published);
        }

        if (query.ReviewDue)
        {
            places = places.Where(p => p.Status == VerificationStatus.Verified && p.LastReviewedAt < reviewCutoff);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim().ToLower();
            places = places.Where(p => p.Name.ToLower().Contains(term) || p.Slug.Contains(term));
        }

        var total = await places.CountAsync(cancellationToken);
        var items = await places
            .OrderByDescending(p => p.UpdatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(p => new PlaceSummary(
                p.Id,
                p.Name,
                p.Slug,
                p.CategoryCode,
                p.Status,
                p.IsPublished,
                p.Status == VerificationStatus.Verified && p.LastReviewedAt < reviewCutoff,
                p.UpdatedAt,
                p.LastReviewedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<PlaceSummary>(items, page, pageSize, total);
    }

    public async Task<PlaceStatusCounts> CountsAsync(CancellationToken cancellationToken)
    {
        var reviewCutoff = time.GetUtcNow() - _reviewInterval;
        var byStatus = await db.Places
            .GroupBy(p => p.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var published = await db.Places.CountAsync(p => p.IsPublished, cancellationToken);
        var reviewDue = await db.Places.CountAsync(
            p => p.Status == VerificationStatus.Verified && p.LastReviewedAt < reviewCutoff,
            cancellationToken);

        int Count(VerificationStatus status) => byStatus.SingleOrDefault(s => s.Status == status)?.Count ?? 0;
        return new PlaceStatusCounts(
            Count(VerificationStatus.Draft),
            Count(VerificationStatus.Pending),
            Count(VerificationStatus.Verified),
            Count(VerificationStatus.Rejected),
            published,
            reviewDue);
    }

    public async Task<Result<PlaceDetail>> GetAsync(Guid id, CancellationToken cancellationToken)
    {
        var place = await db.Places.AsNoTracking().SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        return place is null ? PlaceErrors.NotFound : await ToDetailAsync(place, cancellationToken);
    }

    public async Task<Result<PlaceDetail>> CreateAsync(PlaceInput input, Guid userId, CancellationToken cancellationToken)
    {
        var content = await ValidateAsync(input, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error;
        }

        var place = Place.Create(content.Value, await UniqueSlugAsync(content.Value.Name, cancellationToken), userId, time.GetUtcNow());
        db.Places.Add(place);
        audit.Record(userId, AuditActions.PlaceCreated, AuditEntityTypes.Place, place.Id, new { place.Name, place.Slug });
        await db.SaveChangesAsync(cancellationToken);
        return await ToDetailAsync(place, cancellationToken);
    }

    public async Task<Result<PlaceDetail>> UpdateAsync(
        Guid id,
        uint expectedVersion,
        PlaceInput input,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var content = await ValidateAsync(input, cancellationToken);
        if (!content.IsSuccess)
        {
            return content.Error;
        }

        var place = await db.Places.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (place is null)
        {
            return PlaceErrors.NotFound;
        }

        db.ExpectVersion(place, expectedVersion);
        var before = place.ToContent();
        var statusBefore = place.Status;
        var wasPublished = place.IsPublished;
        if (!place.Update(content.Value, userId, time.GetUtcNow()))
        {
            return await ToDetailAsync(place, cancellationToken);
        }

        audit.Record(userId, AuditActions.PlaceUpdated, AuditEntityTypes.Place, place.Id, new
        {
            changed = ChangedFields(before, content.Value),
            returnedToDraft = statusBefore != place.Status ? new { from = statusBefore, wasPublished } : null,
        });
        return await SaveAsync(place, cancellationToken);
    }

    public Task<Result<PlaceDetail>> SubmitAsync(Guid id, uint version, Guid userId, CancellationToken cancellationToken) =>
        TransitionAsync(id, version, userId, AuditActions.PlaceSubmitted, null, place =>
        {
            if (!place.CanSubmit)
            {
                return PlaceErrors.InvalidTransition("Only draft or rejected places can be submitted for review.");
            }

            if (!place.IsReadyForReview)
            {
                return PlaceErrors.NotReadyForReview;
            }

            place.Submit(userId, time.GetUtcNow());
            return null;
        }, cancellationToken);

    public Task<Result<PlaceDetail>> VerifyAsync(Guid id, uint version, string? source, Guid userId, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("source", source, MaxSourceLength);
        return errors.Any
            ? Task.FromResult(Result<PlaceDetail>.Failure(errors.ToError()))
            : TransitionAsync(id, version, userId, AuditActions.PlaceVerified, new { source = trimmed }, place =>
            {
                if (!place.CanReview)
                {
                    return PlaceErrors.InvalidTransition("Only places pending review can be verified.");
                }

                place.Verify(userId, trimmed, time.GetUtcNow());
                return null;
            }, cancellationToken);
    }

    public Task<Result<PlaceDetail>> RejectAsync(Guid id, uint version, string? reason, Guid userId, CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("reason", reason, MaxNoteLength);
        return errors.Any
            ? Task.FromResult(Result<PlaceDetail>.Failure(errors.ToError()))
            : TransitionAsync(id, version, userId, AuditActions.PlaceRejected, new { reason = trimmed }, place =>
            {
                if (!place.CanReview)
                {
                    return PlaceErrors.InvalidTransition("Only places pending review can be rejected.");
                }

                place.Reject(userId, trimmed, time.GetUtcNow());
                return null;
            }, cancellationToken);
    }

    public Task<Result<PlaceDetail>> PublishAsync(Guid id, uint version, Guid userId, CancellationToken cancellationToken) =>
        TransitionAsync(id, version, userId, AuditActions.PlacePublished, null, place =>
        {
            if (place.Status != VerificationStatus.Verified)
            {
                return PlaceErrors.InvalidTransition("Only verified places can be published.");
            }

            place.Publish(userId, time.GetUtcNow());
            return null;
        }, cancellationToken);

    public Task<Result<PlaceDetail>> UnpublishAsync(Guid id, uint version, Guid userId, CancellationToken cancellationToken) =>
        TransitionAsync(id, version, userId, AuditActions.PlaceUnpublished, null, place =>
        {
            place.Unpublish(userId, time.GetUtcNow());
            return null;
        }, cancellationToken);

    public Task<Result<PlaceDetail>> ConfirmReviewAsync(
        Guid id,
        uint version,
        string? source,
        Guid userId,
        CancellationToken cancellationToken)
    {
        var errors = new FieldErrors();
        var trimmed = errors.Required("source", source, MaxSourceLength);
        return errors.Any
            ? Task.FromResult(Result<PlaceDetail>.Failure(errors.ToError()))
            : TransitionAsync(id, version, userId, AuditActions.PlaceReviewConfirmed, new { source = trimmed }, place =>
            {
                if (place.Status != VerificationStatus.Verified)
                {
                    return PlaceErrors.InvalidTransition("Only verified places can be re-confirmed.");
                }

                place.ConfirmStillAccurate(userId, trimmed, time.GetUtcNow());
                return null;
            }, cancellationToken);
    }

    private async Task<Result<PlaceDetail>> TransitionAsync(
        Guid id,
        uint version,
        Guid userId,
        string auditAction,
        object? auditDetails,
        Func<Place, AppError?> apply,
        CancellationToken cancellationToken)
    {
        var place = await db.Places.SingleOrDefaultAsync(p => p.Id == id, cancellationToken);
        if (place is null)
        {
            return PlaceErrors.NotFound;
        }

        // The reviewer acts on the version they saw, never on content changed since.
        if (place.Version != version)
        {
            return PlaceErrors.Changed;
        }

        db.ExpectVersion(place, version);
        if (apply(place) is { } error)
        {
            return error;
        }

        audit.Record(userId, auditAction, AuditEntityTypes.Place, place.Id, auditDetails);
        return await SaveAsync(place, cancellationToken);
    }

    private async Task<Result<PlaceDetail>> SaveAsync(Place place, CancellationToken cancellationToken)
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            return PlaceErrors.Changed;
        }

        return await ToDetailAsync(place, cancellationToken);
    }

    private async Task<Result<PlaceContent>> ValidateAsync(PlaceInput input, CancellationToken cancellationToken)
    {
        var categories = await db.PlaceCategories.Where(c => c.IsActive).Select(c => c.Code).ToListAsync(cancellationToken);
        return PlaceInputValidator.Validate(input, categories.ToHashSet(StringComparer.Ordinal));
    }

    private async Task<string> UniqueSlugAsync(string name, CancellationToken cancellationToken)
    {
        var slug = Slugs.FromName(name);
        var taken = await db.Places
            .Where(p => p.Slug == slug || p.Slug.StartsWith(slug + "-"))
            .Select(p => p.Slug)
            .ToListAsync(cancellationToken);
        var number = 1;
        while (taken.Contains(Slugs.WithSuffix(slug, number)))
        {
            number++;
        }

        return Slugs.WithSuffix(slug, number);
    }

    private async Task<PlaceDetail> ToDetailAsync(Place place, CancellationToken cancellationToken)
    {
        var media = await db.PlaceMedia
            .AsNoTracking()
            .Where(m => m.PlaceId == place.Id)
            .OrderBy(m => m.SortOrder)
            .ToListAsync(cancellationToken);
        var userIds = new[] { place.CreatedByUserId, place.UpdatedByUserId }
            .Concat(place.VerifiedByUserId is { } verifier ? [verifier] : [])
            .Distinct()
            .ToList();
        var names = await staff.GetNamesAsync(userIds, cancellationToken);
        UserRef User(Guid userId) => new(userId, names.GetValueOrDefault(userId, "Unknown user"));

        return new PlaceDetail(
            place.Id,
            place.Slug,
            place.Version,
            place.Name,
            place.CategoryCode,
            place.Description,
            place.VisitorGuidance,
            place.Address,
            place.Latitude,
            place.Longitude,
            OpeningHoursView.From(place.OpeningHours),
            EntryFeeView.From(place.EntryFees),
            [.. place.Translations
                .OrderBy(t => t.Language, StringComparer.Ordinal)
                .Select(t => new PlaceTranslationContent(t.Language, t.Name, t.Description, t.VisitorGuidance))],
            place.Status,
            place.IsPublished,
            place.IsReadyForReview,
            place.IsReviewDue(time.GetUtcNow(), _reviewInterval),
            place.VerifiedByUserId is { } verifiedBy ? User(verifiedBy) : null,
            place.VerifiedAt,
            place.VerificationSource,
            place.LastReviewedAt,
            place.ReviewNote,
            place.CreatedAt,
            User(place.CreatedByUserId),
            place.UpdatedAt,
            User(place.UpdatedByUserId),
            [.. media.Select(m => MediaView.From(m, storage))]);
    }

    private static List<string> ChangedFields(PlaceContent before, PlaceContent after)
    {
        var changed = new List<string>();
        void Check(string field, bool same)
        {
            if (!same)
            {
                changed.Add(field);
            }
        }

        Check("name", before.Name == after.Name);
        Check("categoryCode", before.CategoryCode == after.CategoryCode);
        Check("description", before.Description == after.Description);
        Check("visitorGuidance", before.VisitorGuidance == after.VisitorGuidance);
        Check("address", before.Address == after.Address);
        Check("location", before.Location == after.Location);
        Check("openingHours", Equals(before.OpeningHours, after.OpeningHours));
        Check("entryFees", before.EntryFees.OrderBy(f => f.Category).SequenceEqual(after.EntryFees.OrderBy(f => f.Category)));
        Check("translations", before.Translations.OrderBy(t => t.Language, StringComparer.Ordinal)
            .SequenceEqual(after.Translations.OrderBy(t => t.Language, StringComparer.Ordinal)));
        return changed;
    }
}
