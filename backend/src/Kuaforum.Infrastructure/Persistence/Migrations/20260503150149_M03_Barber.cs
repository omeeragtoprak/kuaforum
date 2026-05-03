using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuaforum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M03_Barber : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "barber_shops",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    description = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    phone_e164 = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    gender_type = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    address_city_id = table.Column<Guid>(type: "uuid", nullable: true),
                    address_district_id = table.Column<Guid>(type: "uuid", nullable: true),
                    address_neighborhood_id = table.Column<Guid>(type: "uuid", nullable: true),
                    address_line1 = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    address_line2 = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    address_postal_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    latitude = table.Column<double>(type: "double precision", nullable: true),
                    longitude = table.Column<double>(type: "double precision", nullable: true),
                    sub_merchant_key = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    commission_tier = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_barber_shops", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kyc_documents",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    submission_id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_type = table.Column<int>(type: "integer", nullable: false),
                    file_name = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: false),
                    storage_key = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    size_bytes = table.Column<long>(type: "bigint", nullable: false),
                    content_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kyc_documents", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "kyc_submissions",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    submitted_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    rejection_reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reviewed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    reviewed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_kyc_submissions", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "service_catalogs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    gender = table.Column<int>(type: "integer", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    buffer_minutes = table.Column<int>(type: "integer", nullable: false),
                    price_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_service_catalogs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stylist_services",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stylist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_catalog_id = table.Column<Guid>(type: "uuid", nullable: false),
                    override_duration_minutes = table.Column<int>(type: "integer", nullable: true),
                    override_price_minor = table.Column<long>(type: "bigint", nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stylist_services", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "stylists",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    display_name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    photo_url = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    bio = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    walk_in_available = table.Column<bool>(type: "boolean", nullable: false),
                    walk_in_toggled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_stylists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "time_offs",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stylist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    starts_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    reason = table.Column<string>(type: "character varying(250)", maxLength: 250, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_time_offs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "working_hours",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stylist_id = table.Column<Guid>(type: "uuid", nullable: false),
                    day_of_week = table.Column<int>(type: "integer", nullable: false),
                    open_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    close_time = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    is_closed = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_working_hours", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_barber_shops_city_district",
                table: "barber_shops",
                columns: new[] { "address_city_id", "address_district_id" });

            migrationBuilder.CreateIndex(
                name: "ix_barber_shops_lat_lng",
                table: "barber_shops",
                columns: new[] { "latitude", "longitude" });

            migrationBuilder.CreateIndex(
                name: "ix_barber_shops_owner",
                table: "barber_shops",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "ix_barber_shops_status",
                table: "barber_shops",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_barber_shops_slug",
                table: "barber_shops",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_kyc_documents_submission",
                table: "kyc_documents",
                column: "submission_id");

            migrationBuilder.CreateIndex(
                name: "ix_kyc_submissions_shop",
                table: "kyc_submissions",
                column: "barber_shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_kyc_submissions_status",
                table: "kyc_submissions",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ix_services_shop_active",
                table: "service_catalogs",
                columns: new[] { "barber_shop_id", "is_active" });

            migrationBuilder.CreateIndex(
                name: "ux_stylist_services_pair",
                table: "stylist_services",
                columns: new[] { "stylist_id", "service_catalog_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_stylists_shop",
                table: "stylists",
                column: "barber_shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_stylists_shop_active_walkin",
                table: "stylists",
                columns: new[] { "barber_shop_id", "is_active", "walk_in_available" });

            migrationBuilder.CreateIndex(
                name: "ix_stylists_user",
                table: "stylists",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ix_time_offs_stylist_range",
                table: "time_offs",
                columns: new[] { "stylist_id", "starts_at", "ends_at" });

            migrationBuilder.CreateIndex(
                name: "ux_working_hours_stylist_day",
                table: "working_hours",
                columns: new[] { "stylist_id", "day_of_week" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "barber_shops");

            migrationBuilder.DropTable(
                name: "kyc_documents");

            migrationBuilder.DropTable(
                name: "kyc_submissions");

            migrationBuilder.DropTable(
                name: "service_catalogs");

            migrationBuilder.DropTable(
                name: "stylist_services");

            migrationBuilder.DropTable(
                name: "stylists");

            migrationBuilder.DropTable(
                name: "time_offs");

            migrationBuilder.DropTable(
                name: "working_hours");
        }
    }
}
