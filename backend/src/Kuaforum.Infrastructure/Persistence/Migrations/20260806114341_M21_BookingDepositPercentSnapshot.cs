using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuaforum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M21_BookingDepositPercentSnapshot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "deposit_percent_snapshot",
                table: "bookings",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deposit_percent_snapshot",
                table: "bookings");
        }
    }
}
