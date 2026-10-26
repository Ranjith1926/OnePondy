using OnePondy.Application.Places;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Places;

namespace OnePondy.UnitTests.Content;

public class PlaceInputValidatorTests
{
    private static readonly HashSet<string> Categories = ["beach", "heritage"];

    [Fact]
    public void Valid_input_becomes_trimmed_content()
    {
        var input = ContentHarness.Input(name: "  Paradise Beach  ") with
        {
            Translations = [new TranslationInput("ta", " பாரடைஸ் கடற்கரை ", null, null), new TranslationInput("fr", "", "  ", null)],
        };

        var result = PlaceInputValidator.Validate(input, Categories);

        Assert.True(result.IsSuccess);
        Assert.Equal("Paradise Beach", result.Value.Name);
        Assert.Equal("பாரடைஸ் கடற்கரை", Assert.Single(result.Value.Translations).Name);
        Assert.Equal(new TimeOnly(17, 30), result.Value.OpeningHours!.Periods[0].Closes);
    }

    [Fact]
    public void Problems_are_reported_per_field()
    {
        var input = new PlaceInput(
            Name: " ",
            CategoryCode: "casino",
            Description: new string('x', 5001),
            VisitorGuidance: null,
            Address: null,
            Latitude: 11.9,
            Longitude: null,
            OpeningHours: new OpeningHoursInput(false, [new OpeningPeriodInput(DayOfWeek.Monday, "9am", "17:00")], null),
            EntryFees: [new EntryFeeInput(VisitorCategory.Standard, -1m), new EntryFeeInput(VisitorCategory.Child, 10.555m)],
            Translations: [new TranslationInput("de", "Strand", null, null), new TranslationInput("ta", null, "விளக்கம்", null)]);

        var error = PlaceInputValidator.Validate(input, Categories).Error!;

        Assert.Equal("validation_failed", error.Code);
        Assert.Equal(
            ["categoryCode", "description", "entryFees", "longitude", "name", "openingHours", "translations", "translations.ta.name"],
            error.FieldErrors!.Keys.Order());
        Assert.Equal(2, error.FieldErrors["entryFees"].Length);
    }

    [Fact]
    public void Blank_opening_hours_are_treated_as_unknown() =>
        Assert.Null(PlaceInputValidator.Validate(
            ContentHarness.Input() with { OpeningHours = new OpeningHoursInput(false, [], " ") },
            Categories).Value!.OpeningHours);
}

public class PlaceAdminServiceTests
{
    private readonly ContentHarness _harness = new();

    [Fact]
    public async Task Places_get_unique_slugs_and_an_audit_entry()
    {
        var first = (await _harness.Places.CreateAsync(ContentHarness.Input(), _harness.Editor, default)).Value!;
        var second = (await _harness.Places.CreateAsync(ContentHarness.Input(), _harness.Editor, default)).Value!;

        Assert.Equal("paradise-beach", first.Slug);
        Assert.Equal("paradise-beach-2", second.Slug);
        Assert.Equal("Asha", first.CreatedBy.Name);
        Assert.Equal(2, _harness.Db.AuditEntries.Count(e => e.Action == AuditActions.PlaceCreated));
    }

    [Fact]
    public async Task Every_verification_records_the_reviewer_time_and_source()
    {
        var place = await _harness.PublishedPlaceAsync();

        Assert.Equal(VerificationStatus.Verified, place.Status);
        Assert.True(place.IsPublished);
        Assert.Equal("Admin", place.VerifiedBy!.Name);
        Assert.Equal(_harness.Time.GetUtcNow(), place.VerifiedAt);
        Assert.Equal("Site visit", place.VerificationSource);

        var verified = _harness.Db.AuditEntries.Single(e => e.Action == AuditActions.PlaceVerified);
        Assert.Equal(_harness.Administrator, verified.ActorUserId);
        Assert.Contains("Site visit", verified.Details);
    }

    [Fact]
    public async Task Editing_a_published_place_unpublishes_it_and_says_so_in_the_audit_log()
    {
        var place = await _harness.PublishedPlaceAsync();

        var edited = (await _harness.Places.UpdateAsync(
            place.Id,
            place.Version,
            ContentHarness.Input(description: "New boat timings."),
            _harness.Editor,
            default)).Value!;

        Assert.Equal(VerificationStatus.Draft, edited.Status);
        Assert.False(edited.IsPublished);
        var entry = _harness.Db.AuditEntries.Single(e => e.Action == AuditActions.PlaceUpdated);
        Assert.Contains("\"description\"", entry.Details);
        Assert.Contains("\"wasPublished\":true", entry.Details);
    }

    [Fact]
    public async Task Workflow_errors_explain_what_is_wrong()
    {
        var draft = (await _harness.Places.CreateAsync(ContentHarness.Input(latitude: null, longitude: null), _harness.Editor, default)).Value!;

        Assert.Equal("not_ready_for_review", (await _harness.Places.SubmitAsync(draft.Id, draft.Version, _harness.Editor, default)).Error!.Code);
        Assert.Equal("invalid_transition", (await _harness.Places.VerifyAsync(draft.Id, draft.Version, "Source", _harness.Editor, default)).Error!.Code);
        Assert.Equal("invalid_transition", (await _harness.Places.PublishAsync(draft.Id, draft.Version, _harness.Editor, default)).Error!.Code);
        Assert.Equal("place_not_found", (await _harness.Places.SubmitAsync(Guid.NewGuid(), 0, _harness.Editor, default)).Error!.Code);
        Assert.Equal(["source"], (await _harness.Places.VerifyAsync(draft.Id, draft.Version, " ", _harness.Editor, default)).Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Rejected_places_carry_the_reason_and_can_be_resubmitted()
    {
        var draft = (await _harness.Places.CreateAsync(ContentHarness.Input(), _harness.Editor, default)).Value!;
        var pending = (await _harness.Places.SubmitAsync(draft.Id, draft.Version, _harness.Editor, default)).Value!;
        var rejected = (await _harness.Places.RejectAsync(pending.Id, pending.Version, "Check the fee.", _harness.Administrator, default)).Value!;

        Assert.Equal(VerificationStatus.Rejected, rejected.Status);
        Assert.Equal("Check the fee.", rejected.ReviewNote);
        Assert.True((await _harness.Places.SubmitAsync(rejected.Id, rejected.Version, _harness.Editor, default)).IsSuccess);
    }

    [Fact]
    public async Task Places_due_for_review_are_flagged_and_counted()
    {
        var place = await _harness.PublishedPlaceAsync();
        await _harness.Places.CreateAsync(ContentHarness.Input(name: "Ousteri Lake"), _harness.Editor, default);
        _harness.Time.Advance(TimeSpan.FromDays(181));

        var due = await _harness.Places.ListAsync(new PlaceListQuery(null, null, null, null, ReviewDue: true, null, null), default);
        var counts = await _harness.Places.CountsAsync(default);

        Assert.Equal(place.Id, Assert.Single(due.Items).Id);
        Assert.True(due.Items[0].IsReviewDue);
        Assert.Equal(new PlaceStatusCounts(Draft: 1, Pending: 0, Verified: 1, Rejected: 0, Published: 1, ReviewDue: 1), counts);

        var confirmed = (await _harness.Places.ConfirmReviewAsync(place.Id, place.Version, "Phoned the office", _harness.Editor, default)).Value!;
        Assert.False(confirmed.IsReviewDue);
    }

    [Fact]
    public async Task The_list_filters_by_status_and_search()
    {
        await _harness.PublishedPlaceAsync();
        await _harness.Places.CreateAsync(ContentHarness.Input(name: "Ousteri Lake"), _harness.Editor, default);

        var drafts = await _harness.Places.ListAsync(new PlaceListQuery(VerificationStatus.Draft, null, null, null, false, null, null), default);
        var search = await _harness.Places.ListAsync(new PlaceListQuery(null, null, "PARADISE", null, false, null, null), default);

        Assert.Equal("Ousteri Lake", Assert.Single(drafts.Items).Name);
        Assert.Equal("Paradise Beach", Assert.Single(search.Items).Name);
        Assert.Equal(2, (await _harness.Places.ListAsync(new PlaceListQuery(null, null, null, null, false, 1, 500), default)).TotalCount);
    }
}

public class PlaceCatalogTests
{
    private readonly ContentHarness _harness = new();

    [Fact]
    public async Task Only_verified_published_places_are_public()
    {
        var published = await _harness.PublishedPlaceAsync();
        var draft = (await _harness.Places.CreateAsync(ContentHarness.Input(name: "Ousteri Lake"), _harness.Editor, default)).Value!;
        var unpublished = await _harness.PublishedPlaceAsync(ContentHarness.Input(name: "Serenity Beach"));
        await _harness.Places.UnpublishAsync(unpublished.Id, unpublished.Version, _harness.Editor, default);

        var list = await _harness.Catalog.ListAsync(null, null, null, null, null, default);

        Assert.Equal(published.Id, Assert.Single(list.Items).Id);
        Assert.Null(await _harness.Catalog.GetAsync(draft.Id.ToString(), null, default));
        Assert.Null(await _harness.Catalog.GetAsync(unpublished.Slug, null, default));
        Assert.NotNull(await _harness.Catalog.GetAsync(published.Slug, null, default));
    }

    [Fact]
    public async Task Translations_are_used_when_present_and_English_fills_gaps()
    {
        var place = await _harness.PublishedPlaceAsync(ContentHarness.Input(
            translations: [new TranslationInput("ta", "பாரடைஸ் கடற்கரை", null, null)]));

        var tamil = (await _harness.Catalog.GetAsync(place.Slug, "ta", default))!;
        var french = (await _harness.Catalog.GetAsync(place.Slug, "fr", default))!;

        Assert.Equal(("ta", "பாரடைஸ் கடற்கரை"), (tamil.Language, tamil.Name));
        Assert.Equal("A quiet beach reached by boat from Chunnambar.", tamil.Description);
        Assert.Equal(("en", "Paradise Beach"), (french.Language, french.Name));
        Assert.Equal("பாரடைஸ் கடற்கரை", Assert.Single((await _harness.Catalog.ListAsync(null, "பாரடைஸ்", "ta", null, null, default)).Items).Name);
    }
}
