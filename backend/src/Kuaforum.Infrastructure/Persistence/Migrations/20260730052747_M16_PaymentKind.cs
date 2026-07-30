using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuaforum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M16_PaymentKind : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "kind",
                table: "payments",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "kind",
                table: "payments");
        }
    }
}
