namespace OnePondy.Domain.Places;

/// <summary>
/// A tourism place and its review workflow: draft → pending → verified or rejected (spec §10,
/// Phase 3). Customers only see places that are verified and published, so any change to the
/// content sends a reviewed place back to draft and takes it off the app until it is verified again.
/// </summary>
public sealed class Place
{
    private readonly List<PlaceTranslation> _translations = [];
    private readonly List<PlaceEntryFee> _entryFees = [];

    private Place()
    {
    }

    public Guid Id { get; private set; }

    public string Name { get; private set; } = string.Empty;

    /// <summary>Stable URL key, set once from the first name so shared links keep working.</summary>
    public string Slug { get; private set; } = string.Empty;

    public string CategoryCode { get; private set; } = string.Empty;

    public string? Description { get; private set; }

    public string? VisitorGuidance { get; private set; }

    public string? Address { get; private set; }

    public double? Latitude { get; private set; }

    public double? Longitude { get; private set; }

    public OpeningHours? OpeningHours { get; private set; }

    public VerificationStatus Status { get; private set; }

    public Guid? VerifiedByUserId { get; private set; }

    public DateTimeOffset? VerifiedAt { get; private set; }

    /// <summary>Where the facts were checked, e.g. "Site visit 2026-10-20" or an official URL.</summary>
    public string? VerificationSource { get; private set; }

    public DateTimeOffset? LastReviewedAt { get; private set; }

    /// <summary>The reviewer's reason when the content was rejected.</summary>
    public string? ReviewNote { get; private set; }

    public bool IsPublished { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public Guid CreatedByUserId { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public Guid UpdatedByUserId { get; private set; }

    /// <summary>Optimistic concurrency token (PostgreSQL xmin), so two editors cannot overwrite each other.</summary>
    public uint Version { get; private set; }

    public IReadOnlyList<PlaceTranslation> Translations => _translations;

    public IReadOnlyList<PlaceEntryFee> EntryFees => _entryFees;

    public GeoPoint? Location => Latitude is { } lat && Longitude is { } lng ? new GeoPoint(lat, lng) : null;

    public bool CanSubmit => Status is VerificationStatus.Draft or VerificationStatus.Rejected;

    /// <summary>The minimum a reviewer needs: a description and a position on the map.</summary>
    public bool IsReadyForReview => !string.IsNullOrWhiteSpace(Description) && Location is not null;

    public bool CanReview => Status == VerificationStatus.Pending;

    public static Place Create(PlaceContent content, string slug, Guid userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        var place = new Place
        {
            Id = Guid.CreateVersion7(),
            Slug = slug,
            Status = VerificationStatus.Draft,
            CreatedAt = now,
            CreatedByUserId = userId,
        };
        place.Apply(content, userId, now);
        return place;
    }

    public PlaceContent ToContent() => new(
        Name,
        CategoryCode,
        Description,
        VisitorGuidance,
        Address,
        Location,
        OpeningHours,
        [.. _entryFees.Select(f => new EntryFee(f.Category, f.Amount))],
        [.. _translations.Select(t => new PlaceTranslationContent(t.Language, t.Name, t.Description, t.VisitorGuidance))]);

    /// <summary>Applies an edit. Returns false, and changes nothing, when the content is identical.</summary>
    public bool Update(PlaceContent content, Guid userId, DateTimeOffset now)
    {
        if (ToContent().HasSameValuesAs(content))
        {
            return false;
        }

        Apply(content, userId, now);
        if (Status != VerificationStatus.Draft)
        {
            ReturnToDraft();
        }

        return true;
    }

    public void Submit(Guid userId, DateTimeOffset now)
    {
        EnsureState(CanSubmit, "Only draft or rejected places can be submitted for review.");
        EnsureState(IsReadyForReview, "A place needs a description and a location before review.");
        Status = VerificationStatus.Pending;
        ReviewNote = null;
        Touch(userId, now);
    }

    public void Verify(Guid userId, string source, DateTimeOffset now)
    {
        EnsureState(CanReview, "Only places pending review can be verified.");
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        Status = VerificationStatus.Verified;
        RecordReview(userId, source, now);
        Touch(userId, now);
    }

    public void Reject(Guid userId, string reason, DateTimeOffset now)
    {
        EnsureState(CanReview, "Only places pending review can be rejected.");
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        Status = VerificationStatus.Rejected;
        ReviewNote = reason.Trim();
        Touch(userId, now);
    }

    public void Publish(Guid userId, DateTimeOffset now)
    {
        EnsureState(Status == VerificationStatus.Verified, "Only verified places can be published.");
        IsPublished = true;
        Touch(userId, now);
    }

    public void Unpublish(Guid userId, DateTimeOffset now)
    {
        IsPublished = false;
        Touch(userId, now);
    }

    /// <summary>A periodic re-check that the verified facts are still true; resets the review clock.</summary>
    public void ConfirmStillAccurate(Guid userId, string source, DateTimeOffset now)
    {
        EnsureState(Status == VerificationStatus.Verified, "Only verified places can be re-confirmed.");
        ArgumentException.ThrowIfNullOrWhiteSpace(source);
        RecordReview(userId, source, now);
        Touch(userId, now);
    }

    public bool IsReviewDue(DateTimeOffset now, TimeSpan interval) =>
        Status == VerificationStatus.Verified && LastReviewedAt is { } reviewed && now - reviewed > interval;

    private void Apply(PlaceContent content, Guid userId, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(content.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(content.CategoryCode);
        if (content.Location is { IsValid: false })
        {
            throw new ArgumentException("Latitude or longitude is out of range.", nameof(content));
        }

        if (content.EntryFees.Any(f => f.Amount < 0)
            || content.EntryFees.Select(f => f.Category).Distinct().Count() != content.EntryFees.Count)
        {
            throw new ArgumentException("Entry fees must be non-negative and listed once per category.", nameof(content));
        }

        if (content.Translations.Any(t => !ContentLanguages.IsTranslation(t.Language))
            || content.Translations.Select(t => t.Language).Distinct().Count() != content.Translations.Count)
        {
            throw new ArgumentException("Translations must be Tamil or French, each at most once.", nameof(content));
        }

        Name = content.Name;
        CategoryCode = content.CategoryCode;
        Description = content.Description;
        VisitorGuidance = content.VisitorGuidance;
        Address = content.Address;
        Latitude = content.Location?.Latitude;
        Longitude = content.Location?.Longitude;
        OpeningHours = content.OpeningHours;

        // Rows are updated in place, keyed by category and language, rather than replaced.
        _entryFees.RemoveAll(fee => content.EntryFees.All(f => f.Category != fee.Category));
        foreach (var fee in content.EntryFees)
        {
            var existing = _entryFees.Find(f => f.Category == fee.Category);
            if (existing is null)
            {
                _entryFees.Add(new PlaceEntryFee(fee.Category, fee.Amount));
            }
            else
            {
                existing.Amount = fee.Amount;
            }
        }

        _translations.RemoveAll(translation => content.Translations.All(t => t.Language != translation.Language));
        foreach (var translation in content.Translations)
        {
            var existing = _translations.Find(t => t.Language == translation.Language);
            if (existing is null)
            {
                _translations.Add(new PlaceTranslation(translation.Language));
                existing = _translations[^1];
            }

            existing.Name = translation.Name;
            existing.Description = translation.Description;
            existing.VisitorGuidance = translation.VisitorGuidance;
        }

        Touch(userId, now);
    }

    private void ReturnToDraft()
    {
        Status = VerificationStatus.Draft;
        IsPublished = false;
        VerifiedByUserId = null;
        VerifiedAt = null;
        VerificationSource = null;
    }

    private void RecordReview(Guid userId, string source, DateTimeOffset now)
    {
        VerifiedByUserId = userId;
        VerifiedAt = now;
        VerificationSource = source.Trim();
        LastReviewedAt = now;
        ReviewNote = null;
    }

    private void Touch(Guid userId, DateTimeOffset now)
    {
        UpdatedAt = now;
        UpdatedByUserId = userId;
    }

    private static void EnsureState(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }
}

public sealed class PlaceTranslation
{
    private PlaceTranslation()
    {
    }

    internal PlaceTranslation(string language)
    {
        Language = language;
    }

    public string Language { get; private set; } = string.Empty;

    public string Name { get; internal set; } = string.Empty;

    public string? Description { get; internal set; }

    public string? VisitorGuidance { get; internal set; }
}

public sealed class PlaceEntryFee
{
    public const string Currency = "INR";

    private PlaceEntryFee()
    {
    }

    internal PlaceEntryFee(VisitorCategory category, decimal amount)
    {
        Category = category;
        Amount = amount;
    }

    public VisitorCategory Category { get; private set; }

    /// <summary>Rupees, stored as numeric(12,2) (spec D14).</summary>
    public decimal Amount { get; internal set; }
}
