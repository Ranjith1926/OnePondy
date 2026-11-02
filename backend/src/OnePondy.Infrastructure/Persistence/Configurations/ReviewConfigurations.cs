using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OnePondy.Domain.Bookings;
using OnePondy.Domain.Businesses;
using OnePondy.Domain.Food;
using OnePondy.Domain.Listings;
using OnePondy.Domain.Reviews;
using OnePondy.Infrastructure.Identity;

namespace OnePondy.Infrastructure.Persistence.Configurations;

// Reviews (spec §7 and Phase 10, step 10.1). The rules that make a review trustworthy are checked by the database as well as by the service:
// one review per transaction, a rating from 1 to 5, and a status that agrees with who moderated it and when.

public sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews", table =>
        {
            table.HasCheckConstraint("ck_review_rating", $"rating BETWEEN {Review.MinRating} AND {Review.MaxRating}");
            table.HasCheckConstraint("ck_review_text", $"text IS NULL OR char_length(text) BETWEEN 1 AND {Review.MaxTextLength}");
            table.HasCheckConstraint("ck_review_status", $"status IN ({SnakeCaseEnum<ReviewStatus>.SqlList})");

            // A review is of a booking (and so of its listing) or of a food order, never both and never neither.
            table.HasCheckConstraint(
                "ck_review_subject",
                "((booking_id IS NOT NULL) <> (food_order_id IS NOT NULL)) AND ((booking_id IS NOT NULL) = (listing_id IS NOT NULL))");

            // What a status means for the moderation columns: waiting has none; published has a moderator and a time; rejected and taken down
            // have a moderator and the reason the author is shown, and only a taken-down review was ever published.
            table.HasCheckConstraint(
                "ck_review_moderation",
                "(status = 'pending' AND moderated_by_user_id IS NULL AND moderated_at IS NULL AND published_at IS NULL AND moderation_reason IS NULL) "
                + "OR (status = 'published' AND moderated_by_user_id IS NOT NULL AND moderated_at IS NOT NULL AND published_at IS NOT NULL AND moderation_reason IS NULL) "
                + "OR (status = 'rejected' AND moderated_by_user_id IS NOT NULL AND moderated_at IS NOT NULL AND published_at IS NULL AND moderation_reason IS NOT NULL) "
                + "OR (status = 'removed' AND moderated_by_user_id IS NOT NULL AND moderated_at IS NOT NULL AND published_at IS NOT NULL AND moderation_reason IS NOT NULL)");

            // A business's reply is all three columns or none, and only to a review that was published.
            table.HasCheckConstraint(
                "ck_review_reply",
                "((reply_text IS NULL) = (replied_at IS NULL)) AND ((reply_text IS NULL) = (replied_by_user_id IS NULL)) "
                + $"AND (reply_text IS NULL OR (status IN ('published', 'removed') AND char_length(reply_text) BETWEEN 1 AND {Review.MaxReplyLength}))");
        });

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Text).HasMaxLength(Review.MaxTextLength);
        builder.Property(r => r.AuthorName).HasMaxLength(Review.MaxAuthorNameLength).IsRequired();
        builder.Property(r => r.Status).HasConversion(SnakeCaseEnum<ReviewStatus>.Converter).HasMaxLength(16);
        builder.Property(r => r.AutoCheckFlags).HasMaxLength(Review.MaxFlagsLength);
        builder.Property(r => r.ModerationReason).HasMaxLength(Review.MaxReasonLength);
        builder.Property(r => r.ReplyText).HasMaxLength(Review.MaxReplyLength);
        builder.Property(r => r.Version).IsRowVersion();
        builder.Ignore(r => r.HasReply);

        // One review per transaction, enforced here so two requests arriving together cannot both succeed.
        builder.HasIndex(r => r.BookingId).IsUnique().HasFilter("booking_id IS NOT NULL").HasDatabaseName("ux_reviews_booking");
        builder.HasIndex(r => r.FoodOrderId).IsUnique().HasFilter("food_order_id IS NOT NULL").HasDatabaseName("ux_reviews_food_order");

        // The public pages and the ratings read published reviews of one listing or one business; the moderators' queue reads one status.
        builder.HasIndex(r => new { r.ListingId, r.Status }).HasDatabaseName("ix_reviews_listing_status");
        builder.HasIndex(r => new { r.BusinessId, r.Status, r.PublishedAt }).HasDatabaseName("ix_reviews_business_status_published");
        builder.HasIndex(r => new { r.Status, r.CreatedAt }).HasDatabaseName("ix_reviews_status_created");
        builder.HasIndex(r => r.CustomerUserId).HasDatabaseName("ix_reviews_customer");

        // Reviews are removed with the author's account (the service deletes them first); nothing else cascades.
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.CustomerUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.ModeratedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(r => r.RepliedByUserId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Booking>().WithMany().HasForeignKey(r => r.BookingId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FoodOrder>().WithMany().HasForeignKey(r => r.FoodOrderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Business>().WithMany().HasForeignKey(r => r.BusinessId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Listing>().WithMany().HasForeignKey(r => r.ListingId).OnDelete(DeleteBehavior.Restrict);
    }
}
