using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OnePondy.Application.Audit;
using OnePondy.Application.Delivery;
using OnePondy.Application.Reviews;
using OnePondy.Domain.Audit;
using OnePondy.Domain.Delivery;
using OnePondy.Domain.Identity;
using OnePondy.Domain.Reviews;
using OnePondy.UnitTests.Bookings;
using OnePondy.UnitTests.Food;

namespace OnePondy.UnitTests.Reviews;

/// <summary>The automatic check on a review's text: it only raises flags for a moderator, and must not cry wolf at ordinary sentences.</summary>
public class ReviewChecksTests
{
    private static readonly string[] NoWords = [];

    [Theory]
    [InlineData("Lovely place, would go again.")]
    [InlineData("I paid 250 rupees and waited 45 minutes for 2 coffees.")]
    [InlineData("Room 12 on floor 3, booked for 2 nights in 2026.")]
    [InlineData("")]
    [InlineData("   ")]
    public void Ordinary_text_raises_nothing(string text) => Assert.Null(ReviewChecks.Check(text, NoWords));

    [Fact]
    public void Missing_text_raises_nothing() => Assert.Null(ReviewChecks.Check(null, NoWords));

    [Theory]
    [InlineData("call me on 98765 43210")]
    [InlineData("whatsapp +91-98765-43210 for a discount")]
    [InlineData("ring 9876543210 now")]
    [InlineData("office (0413) 233 4455")]
    public void A_phone_number_in_any_common_layout_is_flagged(string text) =>
        Assert.Equal(ReviewChecks.PhoneNumber, ReviewChecks.Check(text, NoWords));

    [Fact]
    public void An_email_address_is_flagged() =>
        Assert.Equal(ReviewChecks.EmailAddress, ReviewChecks.Check("write to owner.name+offers@example.com for more", NoWords));

    [Theory]
    [InlineData("see https://example.com/deal")]
    [InlineData("visit www.best-hotels.in today")]
    [InlineData("book at cheapstay.com instead")]
    public void A_web_address_is_flagged(string text) => Assert.Equal(ReviewChecks.Link, ReviewChecks.Check(text, NoWords));

    [Fact]
    public void Blocked_words_match_whole_words_without_regard_to_case()
    {
        string[] blocked = ["scam"];
        Assert.Equal(ReviewChecks.BlockedWord, ReviewChecks.Check("This is a SCAM!", blocked));
        Assert.Null(ReviewChecks.Check("They were not scammed here, nor were we.", blocked));
        Assert.Null(ReviewChecks.Check("Nothing wrong at all", blocked));
    }

    [Fact]
    public void A_blank_blocked_word_never_matches_everything() => Assert.Null(ReviewChecks.Check("anything", ["", "  "]));

    [Fact]
    public void Several_findings_come_back_in_a_fixed_order_for_the_moderator()
    {
        var flags = ReviewChecks.Check("mail a@b.com, call 9876543210, or see www.x.in, total scam", ["scam"]);

        Assert.Equal("phone_number,email,link,blocked_word", flags);
        Assert.Equal(["phone_number", "email", "link", "blocked_word"], ReviewChecks.Split(flags));
        Assert.Empty(ReviewChecks.Split(null));
    }
}

/// <summary>The review itself: the rules that hold wherever it is used.</summary>
public class ReviewDomainTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 2, 4, 0, 0, TimeSpan.Zero);
    private static readonly Guid Moderator = Guid.NewGuid();

    private static Review Pending(int rating = 4, string? text = "Good food.") =>
        Review.ForFoodOrder(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), rating, text, "Anita", null, Now.AddHours(-2), Now);

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public void A_rating_outside_one_to_five_is_refused(int rating) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => Pending(rating));

    [Fact]
    public void Text_is_optional_but_never_empty_or_longer_than_the_limit()
    {
        Assert.Null(Pending(text: null).Text);
        Assert.Throws<ArgumentException>(() => Pending(text: ""));
        Assert.Throws<ArgumentException>(() => Pending(text: new string('x', Review.MaxTextLength + 1)));
        Assert.Equal(Review.MaxTextLength, Pending(text: new string('x', Review.MaxTextLength)).Text!.Length);
    }

    [Fact]
    public void A_new_review_waits_for_a_moderator()
    {
        var review = Pending();

        Assert.Equal(ReviewStatus.Pending, review.Status);
        Assert.Null(review.PublishedAt);
        Assert.Null(review.ModeratedByUserId);
    }

    [Fact]
    public void A_booking_review_names_its_listing_and_a_food_review_does_not()
    {
        var booking = Review.ForBooking(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 5, null, "Anita", null, Now, Now);

        Assert.NotNull(booking.BookingId);
        Assert.NotNull(booking.ListingId);
        Assert.Null(booking.FoodOrderId);
        Assert.NotNull(Pending().FoodOrderId);
        Assert.Null(Pending().ListingId);
    }

    [Fact]
    public void Publishing_records_who_and_when_and_works_only_from_waiting()
    {
        var review = Pending();

        review.Publish(Moderator, Now.AddMinutes(5));

        Assert.Equal(ReviewStatus.Published, review.Status);
        Assert.Equal(Now.AddMinutes(5), review.PublishedAt);
        Assert.Equal(Moderator, review.ModeratedByUserId);
        Assert.Throws<InvalidOperationException>(() => review.Publish(Moderator, Now));
    }

    [Fact]
    public void Rejecting_needs_a_reason_and_works_only_from_waiting()
    {
        var review = Pending();
        Assert.Throws<ArgumentException>(() => review.Reject(Moderator, " ", Now));

        review.Reject(Moderator, "Contains a phone number.", Now);

        Assert.Equal(ReviewStatus.Rejected, review.Status);
        Assert.Equal("Contains a phone number.", review.ModerationReason);
        Assert.Null(review.PublishedAt);
        Assert.Throws<InvalidOperationException>(() => review.Reject(Moderator, "again", Now));
    }

    [Fact]
    public void Only_a_published_review_can_be_taken_down()
    {
        var review = Pending();
        Assert.Throws<InvalidOperationException>(() => review.Remove(Moderator, "reason", Now));

        review.Publish(Moderator, Now);
        review.Remove(Moderator, "Reported as abusive.", Now.AddDays(1));

        Assert.Equal(ReviewStatus.Removed, review.Status);
        Assert.NotNull(review.PublishedAt);
    }

    [Fact]
    public void A_reply_goes_only_on_a_published_review_and_only_once()
    {
        var review = Pending();
        Assert.Throws<InvalidOperationException>(() => review.Reply(Guid.NewGuid(), "Thank you!", Now));

        review.Publish(Moderator, Now);
        var owner = Guid.NewGuid();
        review.Reply(owner, "Thank you!", Now.AddHours(1));

        Assert.Equal("Thank you!", review.ReplyText);
        Assert.Equal(owner, review.RepliedByUserId);
        Assert.Throws<InvalidOperationException>(() => review.Reply(owner, "Again", Now));
        Assert.Throws<ArgumentException>(() => Pending().Reply(owner, " ", Now));
    }

    [Fact]
    public void Editing_sends_a_published_review_back_to_waiting_and_clears_the_reply_to_the_old_words()
    {
        var review = Pending();
        review.Publish(Moderator, Now);
        review.Reply(Guid.NewGuid(), "Thank you!", Now);

        review.Edit(2, "Actually the food was cold.", "link", Now.AddHours(3));

        Assert.Equal(ReviewStatus.Pending, review.Status);
        Assert.Equal(2, review.Rating);
        Assert.Equal("link", review.AutoCheckFlags);
        Assert.Null(review.PublishedAt);
        Assert.Null(review.ModeratedByUserId);
        Assert.Null(review.ReplyText);
        Assert.NotNull(review.EditedAt);
    }

    [Fact]
    public void A_review_can_be_edited_only_once_and_not_after_it_was_rejected_or_taken_down()
    {
        var review = Pending();
        review.Edit(5, "Better on a second visit.", null, Now);
        Assert.Throws<InvalidOperationException>(() => review.Edit(4, "Third thoughts", null, Now));

        var rejected = Pending();
        rejected.Reject(Moderator, "No.", Now);
        Assert.Throws<InvalidOperationException>(() => rejected.Edit(3, "Please", null, Now));
    }
}

/// <summary>Step 10.1: who may review what, what the public and partners see, and what moderators can do.</summary>
public class ReviewServiceTests
{
    private static readonly CancellationToken None = CancellationToken.None;

    private readonly BookingHarness _b = new();
    private readonly FoodOrderHarness _f = new();
    private readonly ReviewOptions _settings = new();
    private readonly ReviewService _bookingReviews;
    private readonly ReviewService _foodReviews;
    private int _bookings;
    private int _orders;
    private int _phones;

    public ReviewServiceTests()
    {
        _bookingReviews = new ReviewService(_b.Db, new AuditTrail(_b.Db, _b.Time), _b.Accounts, Options.Create(_settings), _b.Time);
        _foodReviews = new ReviewService(_f.Db, _f.B.Audit, _f.B.Accounts, Options.Create(_settings), _f.B.Time);
    }

    private Guid Customer => _b.Customer;

    // --- Setting up transactions ---

    private async Task<Guid> CompletedBookingAsync(Guid? customer = null)
    {
        var who = customer ?? Customer;
        var created = (await _b.Bookings.CreateAsync(who, _b.Input(1, $"review-{++_bookings}"), None)).Value!;
        var accepted = (await _b.Bookings.AcceptAsync(_b.BusinessId, created.Id, created.Version, _b.Owner, None)).Value!;
        Assert.True((await _b.Bookings.CompleteAsync(_b.BusinessId, created.Id, accepted.Version, _b.Owner, None)).IsSuccess);
        return created.Id;
    }

    private async Task<Guid> ConfirmedBookingAsync()
    {
        var created = (await _b.Bookings.CreateAsync(Customer, _b.Input(1, $"review-{++_bookings}"), None)).Value!;
        Assert.True((await _b.Bookings.AcceptAsync(_b.BusinessId, created.Id, created.Version, _b.Owner, None)).IsSuccess);
        return created.Id;
    }

    private async Task OpenRestaurantAsync()
    {
        await _f.OpenAsync(paymentsOn: false);
    }

    private async Task<Guid> DriverAsync()
    {
        var drivers = new DriverService(_f.Db, _f.B.Accounts, _f.B.Audit, _f.B.Time);
        var id = (await _f.B.Accounts.FindOrCreateAsync(OtpChannel.Sms, $"+9190007{++_phones:00000}", None)).Id;
        Assert.True((await drivers.SaveMineAsync(id, new DriverInput("Ravi Kumar", VehicleType.Scooter, "PY01AB1234", $"PY01203000{_phones:00000}", null), None)).IsSuccess);
        Assert.True((await drivers.ApproveAsync(id, 0, _f.B.Administrator, None)).IsSuccess);
        Assert.True((await drivers.SetDutyAsync(id, true, 0, None)).IsSuccess);
        return id;
    }

    /// <summary>An order taken all the way to delivered through the real steps; returns it and the customer who placed it.</summary>
    private async Task<Guid> DeliveredOrderAsync(bool deliver = true)
    {
        var placed = await _f.PlacedAsync($"review-order-{++_orders}");
        Assert.True((await _f.Orders.AcceptAsync(_f.BusinessId, placed.Id, 0, _f.Owner, None)).IsSuccess);
        Assert.True((await _f.Orders.StartPreparingAsync(_f.BusinessId, placed.Id, 0, _f.Owner, None)).IsSuccess);
        Assert.True((await _f.Orders.MarkReadyAsync(_f.BusinessId, placed.Id, 0, _f.Owner, None)).IsSuccess);
        if (!deliver)
        {
            return placed.Id;
        }

        var driver = await DriverAsync();
        var delivery = new DeliveryService(_f.Db, _f.B.Time, _f.Ledger, Options.Create(_f.Food));
        var job = await _f.Db.DeliveryJobs.AsNoTracking().SingleAsync(j => j.FoodOrderId == placed.Id);
        Assert.True((await delivery.AcceptAsync(driver, job.Id, None)).IsSuccess);
        Assert.True((await delivery.PickUpAsync(driver, job.Id, None)).IsSuccess);
        var delivered = await delivery.DeliverAsync(driver, job.Id, null, None);
        Assert.True(delivered.IsSuccess, delivered.Error?.Detail);
        return placed.Id;
    }

    private async Task<MyReview> ReviewedBookingAsync(int rating = 5, string? text = "Wonderful trip.", Guid? customer = null)
    {
        var id = await CompletedBookingAsync(customer);
        var result = await _bookingReviews.CreateAsync(customer ?? Customer, new ReviewInput(id, null, rating, text), None);
        Assert.True(result.IsSuccess, result.Error?.Detail);
        return result.Value!;
    }

    private async Task<AdminReview> PublishedAsync(Guid reviewId)
    {
        var version = (await _b.Db.Reviews.AsNoTracking().SingleAsync(r => r.Id == reviewId)).Version;
        var published = await _bookingReviews.PublishAsync(reviewId, version, _b.Administrator, None);
        Assert.True(published.IsSuccess, published.Error?.Detail);
        return published.Value!;
    }

    private async Task PublishedBookingReviewsAsync(int count, int rating = 4)
    {
        for (var i = 0; i < count; i++)
        {
            await PublishedAsync((await ReviewedBookingAsync(rating)).Id);
        }
    }

    // --- Who may review ---

    [Fact]
    public async Task A_customer_can_review_their_completed_booking_and_it_waits_for_a_moderator()
    {
        await _b.Accounts.UpdateProfileAsync(Customer, "Anita Rao", null, None);
        var bookingId = await CompletedBookingAsync();

        var created = await _bookingReviews.CreateAsync(Customer, new ReviewInput(bookingId, null, 5, "  Wonderful trip.  "), None);

        Assert.True(created.IsSuccess, created.Error?.Detail);
        var review = created.Value!;
        Assert.Equal(ReviewStatus.Pending, review.Status);
        Assert.Equal("Wonderful trip.", review.Text);
        Assert.Equal(bookingId, review.BookingId);
        Assert.True(review.CanEdit);
        var stored = await _b.Db.Reviews.AsNoTracking().SingleAsync();
        Assert.Equal("Anita", stored.AuthorName);
        Assert.Equal(_b.ListingId, stored.ListingId);
        Assert.Equal(_b.BusinessId, stored.BusinessId);
    }

    [Fact]
    public async Task A_booking_that_is_not_completed_cannot_be_reviewed()
    {
        var result = await _bookingReviews.CreateAsync(Customer, new ReviewInput(await ConfirmedBookingAsync(), null, 5, null), None);

        Assert.Equal("review_not_eligible", result.Error!.Code);
    }

    [Fact]
    public async Task Someone_else_cannot_review_a_booking_and_is_not_told_it_exists()
    {
        var bookingId = await CompletedBookingAsync();
        var stranger = (await _b.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000077777", None)).Id;

        var result = await _bookingReviews.CreateAsync(stranger, new ReviewInput(bookingId, null, 5, null), None);

        Assert.Equal("review_transaction_not_found", result.Error!.Code);
        Assert.Empty(await _b.Db.Reviews.ToListAsync());
    }

    [Fact]
    public async Task A_transaction_is_reviewed_once()
    {
        var bookingId = await CompletedBookingAsync();
        Assert.True((await _bookingReviews.CreateAsync(Customer, new ReviewInput(bookingId, null, 5, null), None)).IsSuccess);

        var again = await _bookingReviews.CreateAsync(Customer, new ReviewInput(bookingId, null, 1, "Changed my mind"), None);

        Assert.Equal("review_already_exists", again.Error!.Code);
        Assert.Single(await _b.Db.Reviews.ToListAsync());
    }

    [Fact]
    public async Task The_review_window_is_thirty_days_from_completion()
    {
        var bookingId = await CompletedBookingAsync();
        _b.Time.Advance(TimeSpan.FromDays(29));
        Assert.True((await _bookingReviews.CreateAsync(Customer, new ReviewInput(bookingId, null, 4, null), None)).IsSuccess);

        var late = await CompletedBookingAsync();
        _b.Time.Advance(TimeSpan.FromDays(31));
        var result = await _bookingReviews.CreateAsync(Customer, new ReviewInput(late, null, 4, null), None);

        Assert.Equal("review_window_closed", result.Error!.Code);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(null)]
    public async Task The_rating_must_be_one_to_five(int? rating)
    {
        var result = await _bookingReviews.CreateAsync(Customer, new ReviewInput(await CompletedBookingAsync(), null, rating, null), None);

        Assert.Equal("validation_failed", result.Error!.Code);
        Assert.Contains("rating", result.Error.FieldErrors!.Keys);
    }

    [Fact]
    public async Task The_text_cannot_be_longer_than_the_limit()
    {
        var result = await _bookingReviews.CreateAsync(
            Customer, new ReviewInput(await CompletedBookingAsync(), null, 4, new string('x', Review.MaxTextLength + 1)), None);

        Assert.Contains("text", result.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task A_review_must_name_one_booking_or_one_order_not_both_and_not_neither()
    {
        var neither = await _bookingReviews.CreateAsync(Customer, new ReviewInput(null, null, 5, null), None);
        var both = await _bookingReviews.CreateAsync(Customer, new ReviewInput(await CompletedBookingAsync(), Guid.NewGuid(), 5, null), None);

        Assert.Equal("validation_failed", neither.Error!.Code);
        Assert.Equal("validation_failed", both.Error!.Code);
    }

    [Fact]
    public async Task The_author_shows_as_the_first_name_never_a_number_or_an_address()
    {
        await _b.Accounts.UpdateProfileAsync(Customer, "9876543210", null, None);
        var review = await ReviewedBookingAsync();
        Assert.Equal("A customer", (await _b.Db.Reviews.AsNoTracking().SingleAsync(r => r.Id == review.Id)).AuthorName);

        await _b.Accounts.UpdateProfileAsync(Customer, "me@example.com", null, None);
        var second = await ReviewedBookingAsync();
        Assert.Equal("A customer", (await _b.Db.Reviews.AsNoTracking().SingleAsync(r => r.Id == second.Id)).AuthorName);
    }

    [Fact]
    public async Task The_automatic_check_flags_personal_data_for_the_moderator_without_deciding_anything()
    {
        _settings.BlockedWords.Add("scam");
        var review = await ReviewedBookingAsync(1, "Total scam, call 9876543210 or see www.x.in");

        var queue = await _bookingReviews.AdminListAsync(new AdminReviewQuery(null, null, null), None);

        Assert.Equal(ReviewStatus.Pending, review.Status);
        Assert.Equal(["phone_number", "link", "blocked_word"], queue.Items.Single().Flags);
    }

    // --- Food orders ---

    [Fact]
    public async Task A_delivered_food_order_can_be_reviewed_and_an_undelivered_one_cannot()
    {
        await OpenRestaurantAsync();
        var notYet = await DeliveredOrderAsync(deliver: false);
        var refused = await _foodReviews.CreateAsync(_f.Buyer, new ReviewInput(null, notYet, 5, null), None);
        Assert.Equal("review_not_eligible", refused.Error!.Code);

        var delivered = await DeliveredOrderAsync();
        var created = await _foodReviews.CreateAsync(_f.Buyer, new ReviewInput(null, delivered, 4, "Hot and on time."), None);

        Assert.True(created.IsSuccess, created.Error?.Detail);
        Assert.Equal(delivered, created.Value!.FoodOrderId);
        var stored = await _f.Db.Reviews.AsNoTracking().SingleAsync();
        Assert.Null(stored.ListingId);
        Assert.Equal(_f.BusinessId, stored.BusinessId);
    }

    [Fact]
    public async Task A_restaurant_shows_its_rating_from_published_food_reviews_once_there_are_three()
    {
        await OpenRestaurantAsync();
        var ratings = new[] { 5, 4, 3 };
        for (var i = 0; i < ratings.Length; i++)
        {
            var order = await DeliveredOrderAsync();
            var review = (await _foodReviews.CreateAsync(_f.Buyer, new ReviewInput(null, order, ratings[i], null), None)).Value!;
            Assert.True((await _foodReviews.PublishAsync(review.Id, review.Version, _f.B.Administrator, None)).IsSuccess);
            var shown = (await _f.Restaurants.GetPublicAsync(_f.BusinessId, None)).Value!.Rating;
            Assert.Equal(i == 2 ? 4.0 : (double?)null, shown?.Average);
        }

        var page = (await _foodReviews.ListPublicAsync(null, _f.BusinessId, null, null, None)).Value!;
        Assert.Equal(new RatingSummary(4.0, 3), page.Rating);
        Assert.Equal(3, page.TotalCount);
    }

    // --- What the public sees ---

    [Fact]
    public async Task A_waiting_review_is_not_public_and_does_not_count()
    {
        await ReviewedBookingAsync();

        var page = (await _bookingReviews.ListPublicAsync(_b.ListingId, null, null, null, None)).Value!;

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Null(page.Rating);
    }

    [Fact]
    public async Task A_published_review_is_public_with_the_authors_first_name_and_no_account_details()
    {
        await _b.Accounts.UpdateProfileAsync(Customer, "Anita Rao", null, None);
        var review = await ReviewedBookingAsync(5, "Wonderful trip.");
        await PublishedAsync(review.Id);

        var page = (await _bookingReviews.ListPublicAsync(_b.ListingId, null, null, null, None)).Value!;

        var shown = Assert.Single(page.Items);
        Assert.Equal("Anita", shown.AuthorName);
        Assert.Equal("Wonderful trip.", shown.Text);
        Assert.Equal(5, shown.Rating);
        Assert.Null(shown.Reply);
    }

    [Fact]
    public async Task A_rating_is_shown_only_from_three_published_reviews_and_a_taken_down_one_leaves_the_average()
    {
        await PublishedBookingReviewsAsync(2, rating: 5);
        Assert.Null((await _b.Listings.GetPublicAsync(_b.ListingId, None)).Value!.Rating);

        var third = await ReviewedBookingAsync(2);
        var published = await PublishedAsync(third.Id);
        var summary = (await _b.Listings.GetPublicAsync(_b.ListingId, None)).Value!.Rating;
        Assert.Equal(new RatingSummary(4.0, 3), summary);

        var removed = await _bookingReviews.RemoveAsync(third.Id, new ModerationInput(published.Version, "Reported as abusive."), _b.Administrator, None);
        Assert.True(removed.IsSuccess);
        Assert.Null((await _b.Listings.GetPublicAsync(_b.ListingId, None)).Value!.Rating);
    }

    [Fact]
    public async Task Listing_lists_carry_the_rating_too()
    {
        await PublishedBookingReviewsAsync(3, rating: 4);

        var list = (await _b.Listings.ListPublicAsync(null, null, null, None)).Value!;

        Assert.Equal(new RatingSummary(4.0, 3), list.Items.Single(l => l.Id == _b.ListingId).Rating);
    }

    [Fact]
    public async Task Reviews_of_a_listing_that_is_not_published_answer_not_found()
    {
        var missing = await _bookingReviews.ListPublicAsync(Guid.NewGuid(), null, null, null, None);
        var neither = await _bookingReviews.ListPublicAsync(null, null, null, null, None);

        Assert.Equal("review_target_not_found", missing.Error!.Code);
        Assert.Equal("validation_failed", neither.Error!.Code);
    }

    // --- The customer's own reviews ---

    [Fact]
    public async Task A_customer_sees_their_own_reviews_with_the_reason_when_one_was_rejected()
    {
        var first = await ReviewedBookingAsync(5, "Fine.");
        var second = await ReviewedBookingAsync(1, "Call 9876543210");
        await _bookingReviews.RejectAsync(second.Id, new ModerationInput(0, "Please leave out phone numbers."), _b.Administrator, None);

        var mine = await _bookingReviews.ListMineAsync(Customer, None);

        Assert.Equal(2, mine.Count);
        Assert.Equal("Please leave out phone numbers.", mine.Single(r => r.Id == second.Id).ModerationReason);
        Assert.False(mine.Single(r => r.Id == second.Id).CanEdit);
        Assert.Equal(ReviewStatus.Pending, mine.Single(r => r.Id == first.Id).Status);
        Assert.Empty(await _bookingReviews.ListMineAsync(Guid.NewGuid(), None));
    }

    [Fact]
    public async Task The_author_can_change_a_review_once_and_it_goes_back_to_waiting()
    {
        var review = await ReviewedBookingAsync(5, "Wonderful trip.");
        await PublishedAsync(review.Id);

        var edited = await _bookingReviews.EditAsync(Customer, review.Id, new ReviewEditInput(0, 3, "Good, though the guide was late."), None);

        Assert.True(edited.IsSuccess, edited.Error?.Detail);
        Assert.Equal(ReviewStatus.Pending, edited.Value!.Status);
        Assert.Equal(3, edited.Value.Rating);
        Assert.False(edited.Value.CanEdit);
        Assert.Empty((await _bookingReviews.ListPublicAsync(_b.ListingId, null, null, null, None)).Value!.Items);

        var second = await _bookingReviews.EditAsync(Customer, review.Id, new ReviewEditInput(0, 1, "Worse"), None);
        Assert.Equal("review_already_edited", second.Error!.Code);
    }

    [Fact]
    public async Task A_review_cannot_be_changed_after_the_edit_window()
    {
        var review = await ReviewedBookingAsync();
        _b.Time.Advance(TimeSpan.FromHours(25));

        var result = await _bookingReviews.EditAsync(Customer, review.Id, new ReviewEditInput(0, 2, "Later thoughts"), None);

        Assert.Equal("review_edit_window_closed", result.Error!.Code);
        Assert.False((await _bookingReviews.ListMineAsync(Customer, None)).Single().CanEdit);
    }

    [Fact]
    public async Task Someone_else_cannot_edit_or_delete_a_review()
    {
        var review = await ReviewedBookingAsync();
        var stranger = (await _b.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000088888", None)).Id;

        Assert.Equal("review_not_found", (await _bookingReviews.EditAsync(stranger, review.Id, new ReviewEditInput(0, 1, null), None)).Error!.Code);
        Assert.Equal("review_not_found", (await _bookingReviews.DeleteAsync(stranger, review.Id, None))!.Code);
        Assert.Single(await _b.Db.Reviews.ToListAsync());
    }

    [Fact]
    public async Task A_rejected_review_cannot_be_edited_or_deleted_so_a_decision_cannot_be_got_round()
    {
        var review = await ReviewedBookingAsync();
        await _bookingReviews.RejectAsync(review.Id, new ModerationInput(0, "Not about the trip."), _b.Administrator, None);

        Assert.Equal("review_not_editable", (await _bookingReviews.EditAsync(Customer, review.Id, new ReviewEditInput(0, 5, "Better"), None)).Error!.Code);
        Assert.Equal("review_not_editable", (await _bookingReviews.DeleteAsync(Customer, review.Id, None))!.Code);
        Assert.Single(await _b.Db.Reviews.ToListAsync());
    }

    [Fact]
    public async Task Deleting_a_published_review_takes_it_out_of_the_rating()
    {
        await PublishedBookingReviewsAsync(3, rating: 4);
        Assert.NotNull((await _b.Listings.GetPublicAsync(_b.ListingId, None)).Value!.Rating);

        var one = await _b.Db.Reviews.AsNoTracking().FirstAsync();
        Assert.Null(await _bookingReviews.DeleteAsync(Customer, one.Id, None));

        Assert.Null((await _b.Listings.GetPublicAsync(_b.ListingId, None)).Value!.Rating);
        Assert.Equal(2, (await _bookingReviews.ListPublicAsync(_b.ListingId, null, null, null, None)).Value!.TotalCount);
    }

    // --- The business ---

    [Fact]
    public async Task The_owner_sees_only_published_reviews_of_their_own_business()
    {
        var published = await ReviewedBookingAsync(5, "Wonderful trip.");
        await PublishedAsync(published.Id);
        await ReviewedBookingAsync(1, "Waiting for moderation");

        var seen = (await _bookingReviews.ListForBusinessAsync(_b.BusinessId, _b.Owner, null, null, None)).Value!;
        var stranger = (await _b.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000099999", None)).Id;
        var refused = await _bookingReviews.ListForBusinessAsync(_b.BusinessId, stranger, null, null, None);

        var review = Assert.Single(seen.Items);
        Assert.Equal("booking", review.Kind);
        Assert.Equal("Backwater Kayaking", review.ListingTitle);
        Assert.Equal("review_not_found", refused.Error!.Code);
    }

    [Fact]
    public async Task The_owner_can_answer_a_published_review_once_and_the_public_sees_the_answer()
    {
        var review = await ReviewedBookingAsync();
        var published = await PublishedAsync(review.Id);

        var replied = await _bookingReviews.ReplyAsync(_b.BusinessId, review.Id, _b.Owner, new ReplyInput(published.Version, "  Thank you for coming!  "), None);

        Assert.True(replied.IsSuccess, replied.Error?.Detail);
        Assert.Equal("Thank you for coming!", replied.Value!.Reply!.Text);
        var shown = (await _bookingReviews.ListPublicAsync(_b.ListingId, null, null, null, None)).Value!.Items.Single();
        Assert.Equal("Thank you for coming!", shown.Reply!.Text);
        var again = await _bookingReviews.ReplyAsync(_b.BusinessId, review.Id, _b.Owner, new ReplyInput(0, "Another"), None);
        Assert.Equal("review_cannot_reply", again.Error!.Code);
        Assert.Contains(await _b.Db.AuditEntries.AsNoTracking().ToListAsync(), a => a.Action == AuditActions.ReviewReplied && a.ActorUserId == _b.Owner);
    }

    [Fact]
    public async Task A_review_that_is_still_waiting_cannot_be_answered_and_a_reply_cannot_be_blank()
    {
        var review = await ReviewedBookingAsync();

        var early = await _bookingReviews.ReplyAsync(_b.BusinessId, review.Id, _b.Owner, new ReplyInput(0, "Thanks!"), None);
        Assert.Equal("review_cannot_reply", early.Error!.Code);

        await PublishedAsync(review.Id);
        var blank = await _bookingReviews.ReplyAsync(_b.BusinessId, review.Id, _b.Owner, new ReplyInput(0, "   "), None);
        Assert.Contains("text", blank.Error!.FieldErrors!.Keys);
    }

    [Fact]
    public async Task Another_owner_cannot_answer_a_review_of_someone_elses_business()
    {
        var review = await ReviewedBookingAsync();
        await PublishedAsync(review.Id);
        var stranger = (await _b.Accounts.FindOrCreateAsync(OtpChannel.Sms, "+919000011122", None)).Id;

        var result = await _bookingReviews.ReplyAsync(_b.BusinessId, review.Id, stranger, new ReplyInput(0, "Not mine"), None);

        Assert.Equal("review_not_found", result.Error!.Code);
        Assert.Null((await _b.Db.Reviews.AsNoTracking().SingleAsync()).ReplyText);
    }

    // --- Moderators ---

    [Fact]
    public async Task The_queue_lists_waiting_reviews_oldest_first_and_other_states_on_request()
    {
        var first = await ReviewedBookingAsync(5, "First.");
        _b.Time.Advance(TimeSpan.FromMinutes(5));
        var second = await ReviewedBookingAsync(4, "Second.");
        await PublishedAsync(second.Id);
        _b.Time.Advance(TimeSpan.FromMinutes(5));
        var third = await ReviewedBookingAsync(3, "Third.");

        var waiting = await _bookingReviews.AdminListAsync(new AdminReviewQuery(null, null, null), None);
        var published = await _bookingReviews.AdminListAsync(new AdminReviewQuery(ReviewStatus.Published, null, null), None);

        Assert.Equal([first.Id, third.Id], waiting.Items.Select(r => r.Id));
        Assert.Equal([second.Id], published.Items.Select(r => r.Id));
        Assert.Equal("Backwater Kayaking", waiting.Items[0].ListingTitle);
        Assert.Equal(2, waiting.TotalCount);
    }

    [Fact]
    public async Task Publishing_is_audited_and_works_only_on_a_waiting_review()
    {
        var review = await ReviewedBookingAsync();

        var published = await _bookingReviews.PublishAsync(review.Id, 0, _b.Administrator, None);

        Assert.Equal(ReviewStatus.Published, published.Value!.Status);
        Assert.Contains(
            await _b.Db.AuditEntries.AsNoTracking().ToListAsync(),
            a => a.Action == AuditActions.ReviewPublished && a.EntityType == AuditEntityTypes.Review && a.EntityId == review.Id.ToString() && a.ActorUserId == _b.Administrator);
        Assert.Equal("review_wrong_state", (await _bookingReviews.PublishAsync(review.Id, 0, _b.Administrator, None)).Error!.Code);
        Assert.Equal("review_not_found", (await _bookingReviews.PublishAsync(Guid.NewGuid(), 0, _b.Administrator, None)).Error!.Code);
    }

    [Fact]
    public async Task Rejecting_and_taking_down_need_a_reason_and_are_audited()
    {
        var waiting = await ReviewedBookingAsync();
        var noReason = await _bookingReviews.RejectAsync(waiting.Id, new ModerationInput(0, "  "), _b.Administrator, None);
        Assert.Contains("reason", noReason.Error!.FieldErrors!.Keys);

        var rejected = await _bookingReviews.RejectAsync(waiting.Id, new ModerationInput(0, "Contains a phone number."), _b.Administrator, None);
        Assert.Equal(ReviewStatus.Rejected, rejected.Value!.Status);
        Assert.Equal("Contains a phone number.", rejected.Value.ModerationReason);

        var live = await ReviewedBookingAsync();
        await PublishedAsync(live.Id);
        Assert.Equal("review_wrong_state", (await _bookingReviews.RejectAsync(live.Id, new ModerationInput(0, "Too late"), _b.Administrator, None)).Error!.Code);
        var removed = await _bookingReviews.RemoveAsync(live.Id, new ModerationInput(0, "Reported as abusive."), _b.Administrator, None);
        Assert.Equal(ReviewStatus.Removed, removed.Value!.Status);
        Assert.Equal("review_wrong_state", (await _bookingReviews.RemoveAsync(waiting.Id, new ModerationInput(0, "Not live"), _b.Administrator, None)).Error!.Code);

        var actions = (await _b.Db.AuditEntries.AsNoTracking().ToListAsync()).Select(a => a.Action).ToList();
        Assert.Contains(AuditActions.ReviewRejected, actions);
        Assert.Contains(AuditActions.ReviewRemoved, actions);
    }

    [Fact]
    public async Task What_a_moderator_sees_does_not_include_the_authors_account()
    {
        await ReviewedBookingAsync();

        var row = (await _bookingReviews.AdminListAsync(new AdminReviewQuery(null, null, null), None)).Items.Single();

        Assert.DoesNotContain(typeof(AdminReview).GetProperties(), p => p.Name.Contains("User", StringComparison.Ordinal) || p.Name.Contains("Phone", StringComparison.Ordinal));
        Assert.Equal("A customer", row.AuthorName);
    }
}
