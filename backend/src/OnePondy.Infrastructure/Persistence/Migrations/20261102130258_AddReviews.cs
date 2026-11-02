using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddReviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "reviews",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    customer_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    booking_id = table.Column<Guid>(type: "uuid", nullable: true),
                    food_order_id = table.Column<Guid>(type: "uuid", nullable: true),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    listing_id = table.Column<Guid>(type: "uuid", nullable: true),
                    rating = table.Column<int>(type: "integer", nullable: false),
                    text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    author_name = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    auto_check_flags = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    moderation_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    moderated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    moderated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reply_text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    replied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    replied_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    transaction_completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    edited_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_reviews", x => x.id);
                    table.CheckConstraint("ck_review_moderation", "(status = 'pending' AND moderated_by_user_id IS NULL AND moderated_at IS NULL AND published_at IS NULL AND moderation_reason IS NULL) OR (status = 'published' AND moderated_by_user_id IS NOT NULL AND moderated_at IS NOT NULL AND published_at IS NOT NULL AND moderation_reason IS NULL) OR (status = 'rejected' AND moderated_by_user_id IS NOT NULL AND moderated_at IS NOT NULL AND published_at IS NULL AND moderation_reason IS NOT NULL) OR (status = 'removed' AND moderated_by_user_id IS NOT NULL AND moderated_at IS NOT NULL AND published_at IS NOT NULL AND moderation_reason IS NOT NULL)");
                    table.CheckConstraint("ck_review_rating", "rating BETWEEN 1 AND 5");
                    table.CheckConstraint("ck_review_reply", "((reply_text IS NULL) = (replied_at IS NULL)) AND ((reply_text IS NULL) = (replied_by_user_id IS NULL)) AND (reply_text IS NULL OR (status IN ('published', 'removed') AND char_length(reply_text) BETWEEN 1 AND 1000))");
                    table.CheckConstraint("ck_review_status", "status IN ('pending', 'published', 'rejected', 'removed')");
                    table.CheckConstraint("ck_review_subject", "((booking_id IS NOT NULL) <> (food_order_id IS NOT NULL)) AND ((booking_id IS NOT NULL) = (listing_id IS NOT NULL))");
                    table.CheckConstraint("ck_review_text", "text IS NULL OR char_length(text) BETWEEN 1 AND 1000");
                    table.ForeignKey(
                        name: "fk_reviews_app_users_customer_user_id",
                        column: x => x.customer_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_app_users_moderated_by_user_id",
                        column: x => x.moderated_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_app_users_replied_by_user_id",
                        column: x => x.replied_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_bookings_booking_id",
                        column: x => x.booking_id,
                        principalTable: "bookings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_food_orders_food_order_id",
                        column: x => x.food_order_id,
                        principalTable: "food_orders",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_reviews_listings_listing_id",
                        column: x => x.listing_id,
                        principalTable: "listings",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_business_status_published",
                table: "reviews",
                columns: new[] { "business_id", "status", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_customer",
                table: "reviews",
                column: "customer_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_listing_status",
                table: "reviews",
                columns: new[] { "listing_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_reviews_moderated_by_user_id",
                table: "reviews",
                column: "moderated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_replied_by_user_id",
                table: "reviews",
                column: "replied_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_reviews_status_created",
                table: "reviews",
                columns: new[] { "status", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ux_reviews_booking",
                table: "reviews",
                column: "booking_id",
                unique: true,
                filter: "booking_id IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ux_reviews_food_order",
                table: "reviews",
                column: "food_order_id",
                unique: true,
                filter: "food_order_id IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "reviews");
        }
    }
}
