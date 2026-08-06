using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuaforum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M19_BookingPolicy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "deposit_percent_max",
                table: "platform_settings",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.50m);

            migrationBuilder.AddColumn<decimal>(
                name: "deposit_percent_min",
                table: "platform_settings",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0.05m);

            migrationBuilder.AddColumn<bool>(
                name: "booking_policy_deposit_enabled",
                table: "barber_shops",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "booking_policy_deposit_percent",
                table: "barber_shops",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "booking_policy_max_bookings_per_month",
                table: "barber_shops",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "booking_policy_max_pending_per_customer",
                table: "barber_shops",
                type: "integer",
                nullable: false,
                defaultValue: 2);

            migrationBuilder.AddColumn<int>(
                name: "booking_policy_min_days_between_bookings",
                table: "barber_shops",
                type: "integer",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "deposit_percent_max",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "deposit_percent_min",
                table: "platform_settings");

            migrationBuilder.DropColumn(
                name: "booking_policy_deposit_enabled",
                table: "barber_shops");

            migrationBuilder.DropColumn(
                name: "booking_policy_deposit_percent",
                table: "barber_shops");

            migrationBuilder.DropColumn(
                name: "booking_policy_max_bookings_per_month",
                table: "barber_shops");

            migrationBuilder.DropColumn(
                name: "booking_policy_max_pending_per_customer",
                table: "barber_shops");

            migrationBuilder.DropColumn(
                name: "booking_policy_min_days_between_bookings",
                table: "barber_shops");
        }
    }
}
