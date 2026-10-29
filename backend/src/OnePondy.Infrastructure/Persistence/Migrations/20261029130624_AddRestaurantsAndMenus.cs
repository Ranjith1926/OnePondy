using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OnePondy.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddRestaurantsAndMenus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "menu_categories",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_categories", x => x.id);
                    table.UniqueConstraint("ux_menu_categories_id_business", x => new { x.id, x.business_id });
                    table.CheckConstraint("ck_menu_category_position", "position >= 0");
                    table.ForeignKey(
                        name: "fk_menu_categories_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "restaurant_profiles",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    prep_time_minutes = table.Column<int>(type: "integer", nullable: false),
                    min_order_amount = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    is_accepting_orders = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_restaurant_profiles", x => x.business_id);
                    table.CheckConstraint("ck_restaurant_min_order", "min_order_amount BETWEEN 0 AND 100000");
                    table.CheckConstraint("ck_restaurant_prep_time", "prep_time_minutes BETWEEN 5 AND 180");
                    table.ForeignKey(
                        name: "fk_restaurant_profiles_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "menu_items",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    category_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    price = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    is_veg = table.Column<bool>(type: "boolean", nullable: false),
                    tax_rate_percent = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: false),
                    is_available = table.Column<bool>(type: "boolean", nullable: false),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_menu_items", x => x.id);
                    table.CheckConstraint("ck_menu_item_archived_unavailable", "NOT is_archived OR NOT is_available");
                    table.CheckConstraint("ck_menu_item_position", "position >= 0");
                    table.CheckConstraint("ck_menu_item_price", "price > 0 AND price <= 100000");
                    table.CheckConstraint("ck_menu_item_tax", "tax_rate_percent BETWEEN 0 AND 28");
                    table.ForeignKey(
                        name: "fk_menu_items_businesses_business_id",
                        column: x => x.business_id,
                        principalTable: "businesses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_menu_items_menu_categories_category_id_business_id",
                        columns: x => new { x.category_id, x.business_id },
                        principalTable: "menu_categories",
                        principalColumns: new[] { "id", "business_id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "restaurant_delivery_areas",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    area_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_restaurant_delivery_areas", x => new { x.business_id, x.area_code });
                    table.CheckConstraint("ck_delivery_area_code", "area_code IN ('puducherry', 'whiteTown', 'tamilQuarter', 'auroville', 'lawspet', 'muthialpet', 'reddiarpalayam', 'ariyankuppam', 'villianur')");
                    table.ForeignKey(
                        name: "fk_restaurant_delivery_areas_restaurant_profiles_business_id",
                        column: x => x.business_id,
                        principalTable: "restaurant_profiles",
                        principalColumn: "business_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ux_menu_categories_name",
                table: "menu_categories",
                columns: new[] { "business_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_menu_items_business_category",
                table: "menu_items",
                columns: new[] { "business_id", "category_id" });

            migrationBuilder.CreateIndex(
                name: "ix_menu_items_category_id_business_id",
                table: "menu_items",
                columns: new[] { "category_id", "business_id" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "menu_items");

            migrationBuilder.DropTable(
                name: "restaurant_delivery_areas");

            migrationBuilder.DropTable(
                name: "menu_categories");

            migrationBuilder.DropTable(
                name: "restaurant_profiles");
        }
    }
}
