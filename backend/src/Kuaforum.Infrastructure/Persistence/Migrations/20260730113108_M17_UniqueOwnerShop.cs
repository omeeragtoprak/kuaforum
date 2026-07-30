using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuaforum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M17_UniqueOwnerShop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_barber_shops_owner",
                table: "barber_shops");

            migrationBuilder.CreateIndex(
                name: "ux_barber_shops_owner",
                table: "barber_shops",
                column: "owner_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ux_barber_shops_owner",
                table: "barber_shops");

            migrationBuilder.CreateIndex(
                name: "ix_barber_shops_owner",
                table: "barber_shops",
                column: "owner_id");
        }
    }
}
