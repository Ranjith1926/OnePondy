using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddBusinesses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "businesses",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    business_type = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    phone_number = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    email = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    address = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    gstin = table.Column<string>(type: "character varying(15)", maxLength: 15, nullable: true),
                    fssai_license_number = table.Column<string>(type: "character varying(14)", maxLength: 14, nullable: true),
                    approval_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    review_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_businesses", x => x.id);
                    table.CheckConstraint("ck_business_active_requires_approval", "NOT is_active OR approval_status = 'approved'");
                    table.CheckConstraint("ck_business_approval_status", "approval_status IN ('pending', 'approved', 'rejected', 'suspended')");
                    table.CheckConstraint("ck_business_coordinates", "(latitude IS NULL) = (longitude IS NULL)");
                    table.CheckConstraint("ck_business_latitude", "latitude IS NULL OR latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_business_longitude", "longitude IS NULL OR longitude BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_business_restaurant_fssai", "business_type <> 'restaurant' OR approval_status <> 'approved' OR fssai_license_number IS NOT NULL");
                    table.CheckConstraint("ck_business_type", "business_type IN ('restaurant', 'hotel', 'activity', 'shop', 'tour_operator', 'service')");
                    table.ForeignKey(
                        name: "fk_businesses_app_users_owner_user_id",
                        column: x => x.owner_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "business_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    uploaded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_business_documents", x => x.id);
                    table.CheckConstraint("ck_business_document_kind", "kind IN ('gstin', 'fssai_licence', 'other')");
                    table.ForeignKey(
                        name: "fk_business_documents_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_business_documents_business_id",
                table: "business_documents",
                column: "business_id");

            migrationBuilder.CreateIndex(
                name: "ix_businesses_owner_user_id",
                table: "businesses",
                column: "owner_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_businesses_type_status",
                table: "businesses",
                columns: new[] { "business_type", "approval_status", "is_active" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "business_documents");

            migrationBuilder.DropTable(
                name: "businesses");
        }
    }
}
