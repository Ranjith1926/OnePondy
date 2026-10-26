using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddContentConfigurationAndAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_settings",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    value = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_app_settings", x => x.key);
                    table.ForeignKey(
                        name: "fk_app_settings_app_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    actor_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    action = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    entity_type = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    entity_id = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    details = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "fk_audit_log_app_users_actor_user_id",
                        column: x => x.actor_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            // The audit log is append-only (spec §10, Phase 3): the database refuses edits and deletes.
            migrationBuilder.Sql("""
                CREATE FUNCTION audit_log_is_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_log is append-only';
                END;
                $$;
                CREATE TRIGGER trg_audit_log_append_only BEFORE UPDATE OR DELETE ON audit_log
                    FOR EACH ROW EXECUTE FUNCTION audit_log_is_append_only();
                CREATE TRIGGER trg_audit_log_no_truncate BEFORE TRUNCATE ON audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION audit_log_is_append_only();
                """);

            migrationBuilder.CreateTable(
                name: "module_flags",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_module_flags", x => x.key);
                    table.CheckConstraint("ck_module_flags_status", "status IN ('available', 'coming_soon', 'hidden')");
                    table.ForeignKey(
                        name: "fk_module_flags_app_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "place_categories",
                columns: table => new
                {
                    code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_place_categories", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "places",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    category_code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    visitor_guidance = table.Column<string>(type: "text", nullable: true),
                    address = table.Column<string>(type: "text", nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    opening_hours = table.Column<string>(type: "jsonb", nullable: true),
                    verification_status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    verified_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    verified_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    verification_source = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    last_reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    review_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_places", x => x.id);
                    table.CheckConstraint("ck_places_coordinates", "(latitude IS NULL) = (longitude IS NULL)");
                    table.CheckConstraint("ck_places_latitude", "latitude IS NULL OR latitude BETWEEN -90 AND 90");
                    table.CheckConstraint("ck_places_longitude", "longitude IS NULL OR longitude BETWEEN -180 AND 180");
                    table.CheckConstraint("ck_places_publish_requires_verified", "NOT is_published OR verification_status = 'verified'");
                    table.CheckConstraint("ck_places_verification_provenance", "verification_status <> 'verified' OR (verified_by_user_id IS NOT NULL AND verified_at IS NOT NULL AND verification_source IS NOT NULL)");
                    table.CheckConstraint("ck_places_verification_status", "verification_status IN ('draft', 'pending', 'verified', 'rejected')");
                    table.ForeignKey(
                        name: "fk_places_app_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_places_app_users_updated_by_user_id",
                        column: x => x.updated_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_places_app_users_verified_by_user_id",
                        column: x => x.verified_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_places_place_categories_category_code",
                        column: x => x.category_code,
                        principalTable: "place_categories",
                        principalColumn: "code",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "place_entry_fees",
                columns: table => new
                {
                    visitor_category = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    place_id = table.Column<Guid>(type: "uuid", nullable: false),
                    amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "INR")
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_place_entry_fees", x => new { x.place_id, x.visitor_category });
                    table.CheckConstraint("ck_place_entry_fee_amount", "amount >= 0");
                    table.CheckConstraint("ck_place_entry_fee_category", "visitor_category IN ('standard', 'indian_adult', 'foreign_adult', 'child', 'student', 'senior')");
                    table.ForeignKey(
                        name: "fk_place_entry_fees_places_place_id",
                        column: x => x.place_id,
                        principalTable: "places",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "place_media",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    place_id = table.Column<Guid>(type: "uuid", nullable: false),
                    storage_key = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    width = table.Column<int>(type: "integer", nullable: false),
                    height = table.Column<int>(type: "integer", nullable: false),
                    alt_text = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    caption = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    licence = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    attribution = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    source_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_place_media", x => x.id);
                    table.CheckConstraint("ck_place_media_attribution", "licence IN ('owned', 'cc0') OR attribution IS NOT NULL");
                    table.CheckConstraint("ck_place_media_licence", "licence IN ('owned', 'partner_supplied', 'cc_by', 'cc_by_sa', 'cc0', 'licensed_stock', 'other')");
                    table.ForeignKey(
                        name: "fk_place_media_app_users_created_by_user_id",
                        column: x => x.created_by_user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_place_media_places_place_id",
                        column: x => x.place_id,
                        principalTable: "places",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "place_translations",
                columns: table => new
                {
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    place_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    visitor_guidance = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_place_translations", x => new { x.place_id, x.language });
                    table.CheckConstraint("ck_place_translation_language", "language IN ('ta', 'fr')");
                    table.ForeignKey(
                        name: "fk_place_translations_places_place_id",
                        column: x => x.place_id,
                        principalTable: "places",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.InsertData(
                table: "app_roles",
                columns: new[] { "id", "concurrency_stamp", "name", "normalized_name" },
                values: new object[,]
                {
                    { new Guid("019a2f3e-1c4d-7e5f-8a6b-2c3d4e5f6a01"), "5b7c2d1e-8f9a-4b3c-9d2e-1f0a2b3c4d01", "administrator", "ADMINISTRATOR" },
                    { new Guid("019a2f3e-1c4d-7e5f-8a6b-2c3d4e5f6a02"), "5b7c2d1e-8f9a-4b3c-9d2e-1f0a2b3c4d02", "content_editor", "CONTENT_EDITOR" },
                    { new Guid("019a2f3e-1c4d-7e5f-8a6b-2c3d4e5f6a03"), "5b7c2d1e-8f9a-4b3c-9d2e-1f0a2b3c4d03", "support_agent", "SUPPORT_AGENT" }
                });

            migrationBuilder.InsertData(
                table: "app_settings",
                columns: new[] { "key", "updated_at", "updated_by_user_id", "value" },
                values: new object[] { "min_supported_app_version", null, null, "0.1.0" });

            migrationBuilder.InsertData(
                table: "module_flags",
                columns: new[] { "key", "status", "updated_at", "updated_by_user_id" },
                values: new object[,]
                {
                    { "activities", "coming_soon", null, null },
                    { "food", "coming_soon", null, null },
                    { "notifications", "coming_soon", null, null },
                    { "rides", "coming_soon", null, null },
                    { "stays", "coming_soon", null, null },
                    { "support", "coming_soon", null, null },
                    { "tripPlanner", "coming_soon", null, null }
                });

            migrationBuilder.InsertData(
                table: "place_categories",
                columns: new[] { "code", "is_active", "name", "sort_order" },
                values: new object[,]
                {
                    { "beach", true, "Beach", 1 },
                    { "heritage", true, "Heritage", 2 },
                    { "museum", true, "Museum", 4 },
                    { "nature", true, "Nature", 5 },
                    { "spiritual", true, "Spiritual", 3 }
                });

            migrationBuilder.CreateIndex(
                name: "ix_app_settings_updated_by_user_id",
                table: "app_settings",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_actor_user_id",
                table: "audit_log",
                column: "actor_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_entity_type_entity_id_occurred_at",
                table: "audit_log",
                columns: new[] { "entity_type", "entity_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_audit_log_occurred_at",
                table: "audit_log",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_module_flags_updated_by_user_id",
                table: "module_flags",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_place_media_created_by_user_id",
                table: "place_media",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_place_media_place_id_sort_order",
                table: "place_media",
                columns: new[] { "place_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_places_console_list",
                table: "places",
                columns: new[] { "verification_status", "updated_at" });

            migrationBuilder.CreateIndex(
                name: "ix_places_created_by_user_id",
                table: "places",
                column: "created_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_places_public_category",
                table: "places",
                column: "category_code",
                filter: "is_published");

            migrationBuilder.CreateIndex(
                name: "ix_places_review_due",
                table: "places",
                column: "last_reviewed_at",
                filter: "is_published");

            migrationBuilder.CreateIndex(
                name: "ix_places_slug",
                table: "places",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_places_updated_by_user_id",
                table: "places",
                column: "updated_by_user_id");

            migrationBuilder.CreateIndex(
                name: "ix_places_verified_by_user_id",
                table: "places",
                column: "verified_by_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "app_settings");

            migrationBuilder.DropTable(
                name: "audit_log");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS audit_log_is_append_only();");

            migrationBuilder.DropTable(
                name: "module_flags");

            migrationBuilder.DropTable(
                name: "place_entry_fees");

            migrationBuilder.DropTable(
                name: "place_media");

            migrationBuilder.DropTable(
                name: "place_translations");

            migrationBuilder.DropTable(
                name: "places");

            migrationBuilder.DropTable(
                name: "place_categories");

            migrationBuilder.DeleteData(
                table: "app_roles",
                keyColumn: "id",
                keyValue: new Guid("019a2f3e-1c4d-7e5f-8a6b-2c3d4e5f6a01"));

            migrationBuilder.DeleteData(
                table: "app_roles",
                keyColumn: "id",
                keyValue: new Guid("019a2f3e-1c4d-7e5f-8a6b-2c3d4e5f6a02"));

            migrationBuilder.DeleteData(
                table: "app_roles",
                keyColumn: "id",
                keyValue: new Guid("019a2f3e-1c4d-7e5f-8a6b-2c3d4e5f6a03"));
        }
    }
}
