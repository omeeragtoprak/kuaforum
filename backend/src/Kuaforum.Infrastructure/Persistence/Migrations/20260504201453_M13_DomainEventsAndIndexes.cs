using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuaforum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M13_DomainEventsAndIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_payments_customer_created",
                table: "payments",
                columns: new[] { "customer_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_customer_scheduled",
                table: "bookings",
                columns: new[] { "customer_id", "scheduled_at" });

            migrationBuilder.CreateIndex(
                name: "ix_bookings_scheduled_at",
                table: "bookings",
                column: "scheduled_at");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_payments_customer_created",
                table: "payments");

            migrationBuilder.DropIndex(
                name: "ix_bookings_customer_scheduled",
                table: "bookings");

            migrationBuilder.DropIndex(
                name: "ix_bookings_scheduled_at",
                table: "bookings");
        }
    }
}
