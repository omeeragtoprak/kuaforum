using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuaforum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M02_Geography : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "cities",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    plate_code = table.Column<int>(type: "integer", nullable: false),
                    slug = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_cities", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "districts",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    city_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    slug = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_districts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "neighborhoods",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    district_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    slug = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: false),
                    postal_code = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_neighborhoods", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_cities_plate",
                table: "cities",
                column: "plate_code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_cities_slug",
                table: "cities",
                column: "slug",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_districts_city",
                table: "districts",
                column: "city_id");

            migrationBuilder.CreateIndex(
                name: "ux_districts_city_slug",
                table: "districts",
                columns: new[] { "city_id", "slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_neighborhoods_district",
                table: "neighborhoods",
                column: "district_id");

            migrationBuilder.CreateIndex(
                name: "ix_neighborhoods_postal",
                table: "neighborhoods",
                column: "postal_code");

            migrationBuilder.CreateIndex(
                name: "ux_neighborhoods_district_slug",
                table: "neighborhoods",
                columns: new[] { "district_id", "slug" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "cities");

            migrationBuilder.DropTable(
                name: "districts");

            migrationBuilder.DropTable(
                name: "neighborhoods");
        }
    }
}
