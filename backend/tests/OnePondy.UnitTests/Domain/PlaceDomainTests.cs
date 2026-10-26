using OnePondy.Domain.Configuration;
using OnePondy.Domain.Places;

namespace OnePondy.UnitTests.Domain;

public class PlaceDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 26, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid Editor = Guid.NewGuid();
    private static readonly Guid Reviewer = Guid.NewGuid();

    internal static PlaceContent Content(
        string name = "Paradise Beach",
        string? description = "A quiet beach reached by boat from Chunnambar.",
        GeoPoint? location = null,
        IReadOnlyList<EntryFee>? fees = null,
        IReadOnlyList<PlaceTranslationContent>? translations = null) =>
        new(
            name,
            "beach",
            description,
            "Carry water; there is little shade.",
            "Chunnambar, Puducherry",
            location ?? new GeoPoint(11.8915, 79.8257),
            null,
            fees ?? [new EntryFee(VisitorCategory.Standard, 50m)],
            translations ?? []);

    private static Place VerifiedPlace()
    {
        var place = Place.Create(Content(), "paradise-beach", Editor, Now);
        place.Submit(Editor, Now);
        place.Verify(Reviewer, "Site visit 2026-10-20", Now);
        return place;
    }

    [Fact]
    public void A_new_place_is_an_unpublished_draft()
    {
        var place = Place.Create(Content(), "paradise-beach", Editor, Now);

        Assert.Equal(VerificationStatus.Draft, place.Status);
        Assert.False(place.IsPublished);
        Assert.Equal("paradise-beach", place.Slug);
        Assert.Equal(Editor, place.CreatedByUserId);
        Assert.True(place.IsReadyForReview);
    }

    [Fact]
    public void Verification_records_who_when_and_the_source()
    {
        var place = VerifiedPlace();

        Assert.Equal(VerificationStatus.Verified, place.Status);
        Assert.Equal(Reviewer, place.VerifiedByUserId);
        Assert.Equal(Now, place.VerifiedAt);
        Assert.Equal("Site visit 2026-10-20", place.VerificationSource);
        Assert.Equal(Now, place.LastReviewedAt);
    }

    [Fact]
    public void Changing_a_published_place_returns_it_to_draft_and_takes_it_off_the_app()
    {
        var place = VerifiedPlace();
        place.Publish(Reviewer, Now);

        var changed = place.Update(Content(description: "Updated boat timings."), Editor, Now.AddDays(1));

        Assert.True(changed);
        Assert.Equal(VerificationStatus.Draft, place.Status);
        Assert.False(place.IsPublished);
        Assert.Null(place.VerifiedByUserId);
        Assert.Null(place.VerificationSource);
    }

    [Fact]
    public void Saving_identical_content_changes_nothing()
    {
        var place = VerifiedPlace();
        place.Publish(Reviewer, Now);

        Assert.False(place.Update(Content(), Editor, Now.AddDays(1)));
        Assert.Equal(VerificationStatus.Verified, place.Status);
        Assert.True(place.IsPublished);
    }

    [Fact]
    public void Rejection_keeps_the_reason_until_the_place_is_resubmitted()
    {
        var place = Place.Create(Content(), "paradise-beach", Editor, Now);
        place.Submit(Editor, Now);
        place.Reject(Reviewer, "Boat timings are out of date.", Now);

        Assert.Equal(VerificationStatus.Rejected, place.Status);
        Assert.Equal("Boat timings are out of date.", place.ReviewNote);

        place.Submit(Editor, Now);
        Assert.Equal(VerificationStatus.Pending, place.Status);
        Assert.Null(place.ReviewNote);
    }

    [Fact]
    public void A_place_without_a_description_or_location_cannot_be_submitted()
    {
        var noDescription = Place.Create(Content(description: null), "a", Editor, Now);

        Assert.False(noDescription.IsReadyForReview);
        Assert.Throws<InvalidOperationException>(() => noDescription.Submit(Editor, Now));
    }

    [Fact]
    public void Workflow_steps_out_of_order_are_refused()
    {
        var draft = Place.Create(Content(), "paradise-beach", Editor, Now);

        Assert.Throws<InvalidOperationException>(() => draft.Verify(Reviewer, "Source", Now));
        Assert.Throws<InvalidOperationException>(() => draft.Reject(Reviewer, "Reason", Now));
        Assert.Throws<InvalidOperationException>(() => draft.Publish(Reviewer, Now));
        Assert.Throws<InvalidOperationException>(() => draft.ConfirmStillAccurate(Reviewer, "Source", Now));
    }

    [Fact]
    public void A_review_falls_due_after_the_interval_and_confirmation_resets_it()
    {
        var place = VerifiedPlace();
        var interval = TimeSpan.FromDays(180);

        Assert.False(place.IsReviewDue(Now.AddDays(180), interval));
        Assert.True(place.IsReviewDue(Now.AddDays(181), interval));

        place.ConfirmStillAccurate(Reviewer, "Phoned the ticket office", Now.AddDays(181));
        Assert.False(place.IsReviewDue(Now.AddDays(181), interval));
        Assert.Equal("Phoned the ticket office", place.VerificationSource);
    }

    [Fact]
    public void Translations_and_fees_are_updated_in_place_by_key()
    {
        var place = Place.Create(
            Content(translations: [new PlaceTranslationContent("ta", "பாரடைஸ் கடற்கரை", null, null)]),
            "paradise-beach",
            Editor,
            Now);
        var tamil = place.Translations.Single();

        place.Update(
            Content(
                fees: [new EntryFee(VisitorCategory.Standard, 60m), new EntryFee(VisitorCategory.Child, 0m)],
                translations:
                [
                    new PlaceTranslationContent("ta", "பாரடைஸ் பீச்", null, null),
                    new PlaceTranslationContent("fr", "Plage du Paradis", null, null),
                ]),
            Editor,
            Now);

        Assert.Same(tamil, place.Translations.Single(t => t.Language == "ta"));
        Assert.Equal("பாரடைஸ் பீச்", tamil.Name);
        Assert.Equal(2, place.Translations.Count);
        Assert.Equal(60m, place.EntryFees.Single(f => f.Category == VisitorCategory.Standard).Amount);

        place.Update(Content(fees: [], translations: []), Editor, Now);
        Assert.Empty(place.Translations);
        Assert.Empty(place.EntryFees);
    }

    [Fact]
    public void Content_rules_are_enforced_even_without_the_validator()
    {
        Assert.Throws<ArgumentException>(() => Place.Create(Content(location: new GeoPoint(95, 0)), "a", Editor, Now));
        Assert.Throws<ArgumentException>(() => Place.Create(
            Content(fees: [new EntryFee(VisitorCategory.Standard, 1), new EntryFee(VisitorCategory.Standard, 2)]),
            "a",
            Editor,
            Now));
        Assert.Throws<ArgumentException>(() => Place.Create(
            Content(translations: [new PlaceTranslationContent("de", "Strand", null, null)]),
            "a",
            Editor,
            Now));
    }
}

public class OpeningHoursTests
{
    private static OpeningPeriod Period(DayOfWeek day, string opens, string closes) =>
        new(day, TimeOnly.Parse(opens, System.Globalization.CultureInfo.InvariantCulture), TimeOnly.Parse(closes, System.Globalization.CultureInfo.InvariantCulture));

    [Fact]
    public void Periods_are_sorted_and_compared_by_value()
    {
        var (first, _) = OpeningHours.Create(false, [Period(DayOfWeek.Tuesday, "14:00", "17:00"), Period(DayOfWeek.Monday, "09:00", "12:00")], " Closed on holidays ");
        var (second, _) = OpeningHours.Create(false, [Period(DayOfWeek.Monday, "09:00", "12:00"), Period(DayOfWeek.Tuesday, "14:00", "17:00")], "Closed on holidays");

        Assert.Equal(DayOfWeek.Monday, first!.Periods[0].Day);
        Assert.Equal("Closed on holidays", first.Notes);
        Assert.Equal(first, second);
    }

    [Fact]
    public void Invalid_hours_are_reported()
    {
        Assert.NotEmpty(OpeningHours.Create(false, [Period(DayOfWeek.Monday, "12:00", "09:00")], null).Problems);
        Assert.NotEmpty(OpeningHours.Create(
            false,
            [Period(DayOfWeek.Monday, "09:00", "13:00"), Period(DayOfWeek.Monday, "12:00", "17:00")],
            null).Problems);
        Assert.NotEmpty(OpeningHours.Create(true, [Period(DayOfWeek.Monday, "09:00", "17:00")], null).Problems);
        Assert.NotEmpty(OpeningHours.Create(false, [], new string('x', 501)).Problems);
    }
}

public class SlugAndVersionTests
{
    [Theory]
    [InlineData("Sri Aurobindo Ashram", "sri-aurobindo-ashram")]
    [InlineData("  Église Notre-Dame des Anges ", "eglise-notre-dame-des-anges")]
    [InlineData("Rock Beach (Promenade)", "rock-beach-promenade")]
    [InlineData("மணக்குள விநாயகர் கோயில்", "place")]
    public void Slugs_are_readable_ascii(string name, string expected) => Assert.Equal(expected, Slugs.FromName(name));

    [Fact]
    public void Slug_suffixes_start_at_two() =>
        Assert.Equal(["beach", "beach-2"], [Slugs.WithSuffix("beach", 1), Slugs.WithSuffix("beach", 2)]);

    [Theory]
    [InlineData("1.2.3", true)]
    [InlineData(" 0.10.0 ", true)]
    [InlineData("1.2", false)]
    [InlineData("1.2.x", false)]
    [InlineData("-1.2.3", false)]
    [InlineData(null, false)]
    public void App_versions_must_be_major_minor_patch(string? value, bool valid) =>
        Assert.Equal(valid, AppVersion.TryParse(value, out _));

    [Fact]
    public void App_versions_compare_numerically()
    {
        AppVersion.TryParse("0.10.0", out var newer);
        AppVersion.TryParse("0.9.9", out var older);

        Assert.True(newer.CompareTo(older) > 0);
    }

    [Theory]
    [InlineData(MediaLicence.Owned, false)]
    [InlineData(MediaLicence.Cc0, false)]
    [InlineData(MediaLicence.CcBy, true)]
    [InlineData(MediaLicence.PartnerSupplied, true)]
    [InlineData(MediaLicence.LicensedStock, true)]
    public void Credit_is_required_unless_we_own_the_photo_or_it_is_public_domain(MediaLicence licence, bool required) =>
        Assert.Equal(required, licence.RequiresAttribution());

    [Fact]
    public void A_photo_needing_credit_cannot_be_saved_without_it() =>
        Assert.Throws<ArgumentException>(() => PlaceMedia.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "places/a/b",
            1600,
            900,
            new MediaDetails("Boats at the jetty", null, MediaLicence.CcBy, null, null),
            0,
            Guid.NewGuid(),
            DateTimeOffset.UtcNow));
}
