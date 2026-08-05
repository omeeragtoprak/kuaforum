using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuaforum.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class M18_CommissionBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "commission_rate_override",
                table: "barber_shops",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "commission_payments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    commission_statement_id = table.Column<Guid>(type: "uuid", nullable: false),
                    provider = table.Column<int>(type: "integer", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    provider_transaction_id = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    failure_reason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    captured_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    idempotency_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commission_payments", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "commission_statements",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    barber_shop_id = table.Column<Guid>(type: "uuid", nullable: false),
                    period_start = table.Column<DateOnly>(type: "date", nullable: false),
                    period_end = table.Column<DateOnly>(type: "date", nullable: false),
                    basis_amount_minor = table.Column<long>(type: "bigint", nullable: false),
                    rate_applied = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    amount_due_minor = table.Column<long>(type: "bigint", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    is_disputed = table.Column<bool>(type: "boolean", nullable: false),
                    dispute_note = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    disputed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    admin_adjustment_note = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_commission_statements", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "platform_settings",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    commission_rate_global = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: false),
                    commission_payment_iban = table.Column<string>(type: "character varying(34)", maxLength: 34, nullable: true),
                    commission_payment_bank_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    commission_payment_account_holder_name = table.Column<string>(type: "character varying(120)", maxLength: 120, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_platform_settings", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_commission_payments_shop",
                table: "commission_payments",
                column: "barber_shop_id");

            migrationBuilder.CreateIndex(
                name: "ix_commission_payments_statement",
                table: "commission_payments",
                column: "commission_statement_id");

            migrationBuilder.CreateIndex(
                name: "ux_commission_payments_idempotency",
                table: "commission_payments",
                column: "idempotency_key",
                unique: true,
                filter: "idempotency_key IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_commission_statements_due_date",
                table: "commission_statements",
                column: "due_date");

            migrationBuilder.CreateIndex(
                name: "ix_commission_statements_status",
                table: "commission_statements",
                column: "status");

            migrationBuilder.CreateIndex(
                name: "ux_commission_statements_shop_period",
                table: "commission_statements",
                columns: new[] { "barber_shop_id", "period_start" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "commission_payments");

            migrationBuilder.DropTable(
                name: "commission_statements");

            migrationBuilder.DropTable(
                name: "platform_settings");

            migrationBuilder.DropColumn(
                name: "commission_rate_override",
                table: "barber_shops");
        }
    }
}
