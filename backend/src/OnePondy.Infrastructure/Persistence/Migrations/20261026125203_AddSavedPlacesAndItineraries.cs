using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSavedPlacesAndItineraries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "itineraries",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    title = table.Column<string>(type: "character varying(180)", maxLength: 180, nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: true),
                    end_date = table.Column<DateOnly>(type: "date", nullable: true),
                    status = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                    source = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    share_token = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itineraries", x => x.id);
                    table.CheckConstraint("ck_itinerary_dates", "end_date IS NULL OR start_date IS NULL OR end_date >= start_date");
                    table.CheckConstraint("ck_itinerary_source", "source IN ('manual', 'ai_assisted')");
                    table.CheckConstraint("ck_itinerary_status", "status IN ('draft', 'saved', 'archived')");
                    table.ForeignKey(
                        name: "fk_itineraries_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "saved_places",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    place_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_saved_places", x => new { x.user_id, x.place_id });
                    table.ForeignKey(
                        name: "fk_saved_places_app_users_user_id",
                        column: x => x.user_id,
                        principalTable: "app_users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_saved_places_places_place_id",
                        column: x => x.place_id,
                        principalTable: "places",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "itinerary_stops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    itinerary_id = table.Column<Guid>(type: "uuid", nullable: false),
                    place_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_number = table.Column<int>(type: "integer", nullable: false),
                    stop_order = table.Column<int>(type: "integer", nullable: false),
                    planned_arrival = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_itinerary_stops", x => x.id);
                    table.CheckConstraint("ck_itinerary_stop_day", "day_number >= 1");
                    table.CheckConstraint("ck_itinerary_stop_order", "stop_order >= 0");
                    table.ForeignKey(
                        name: "fk_itinerary_stops_itineraries_itinerary_id",
                        column: x => x.itinerary_id,
                        principalTable: "itineraries",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_itinerary_stops_places_place_id",
                        column: x => x.place_id,
                        principalTable: "places",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_itineraries_share_token",
                table: "itineraries",
                column: "share_token",
                unique: true,
                filter: "share_token IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_itineraries_user_id",
                table: "itineraries",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_itinerary_stops_itinerary_id_day_number_stop_order",
                table: "itinerary_stops",
                columns: new[] { "itinerary_id", "day_number", "stop_order" });

            migrationBuilder.CreateIndex(
                name: "ix_itinerary_stops_place_id",
                table: "itinerary_stops",
                column: "place_id");

            migrationBuilder.CreateIndex(
                name: "ix_saved_places_place_id",
                table: "saved_places",
                column: "place_id");

            // Deferred so a reorder can move several stops through each other's old positions within
            // one transaction (spec §17.3); the plain index above is for lookups only.
            migrationBuilder.Sql("""
                ALTER TABLE itinerary_stops
                    ADD CONSTRAINT ux_itinerary_stop_order UNIQUE (itinerary_id, day_number, stop_order)
                    DEFERRABLE INITIALLY DEFERRED;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "itinerary_stops");

            migrationBuilder.DropTable(
                name: "saved_places");

            migrationBuilder.DropTable(
                name: "itineraries");
        }
    }
}
