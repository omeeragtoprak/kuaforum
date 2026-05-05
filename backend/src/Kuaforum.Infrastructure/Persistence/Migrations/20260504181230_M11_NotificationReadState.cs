using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuaforum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M11_NotificationReadState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "is_read",
                table: "notification_logs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "read_at",
                table: "notification_logs",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_notification_logs_user_channel_read",
                table: "notification_logs",
                columns: new[] { "user_id", "channel", "is_read" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notification_logs_user_channel_read",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "is_read",
                table: "notification_logs");

            migrationBuilder.DropColumn(
                name: "read_at",
                table: "notification_logs");
        }
    }
}
