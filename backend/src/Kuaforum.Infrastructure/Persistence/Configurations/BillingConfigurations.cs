using Kuaforum.Domain.Modules.Billing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class PlatformSettingsConfiguration : IEntityTypeConfiguration<PlatformSettings>
{
    public void Configure(EntityTypeBuilder<PlatformSettings> builder)
    {
        builder.ToTable("platform_settings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CommissionRateGlobal).HasColumnName("commission_rate_global").HasPrecision(5, 4);
        builder.Property(x => x.CommissionPaymentIban).HasColumnName("commission_payment_iban").HasMaxLength(34);
        builder.Property(x => x.CommissionPaymentBankName).HasColumnName("commission_payment_bank_name").HasMaxLength(120);
        builder.Property(x => x.CommissionPaymentAccountHolderName).HasColumnName("commission_payment_account_holder_name").HasMaxLength(120);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class CommissionStatementConfiguration : IEntityTypeConfiguration<CommissionStatement>
{
    public void Configure(EntityTypeBuilder<CommissionStatement> builder)
    {
        builder.ToTable("commission_statements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BarberShopId).HasColumnName("barber_shop_id");
        builder.Property(x => x.PeriodStart).HasColumnName("period_start");
        builder.Property(x => x.PeriodEnd).HasColumnName("period_end");
        builder.Property(x => x.BasisAmountMinor).HasColumnName("basis_amount_minor");
        builder.Property(x => x.RateApplied).HasColumnName("rate_applied").HasPrecision(5, 4);
        builder.Property(x => x.AmountDueMinor).HasColumnName("amount_due_minor");
        builder.Property(x => x.DueDate).HasColumnName("due_date");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.IsDisputed).HasColumnName("is_disputed");
        builder.Property(x => x.DisputeNote).HasColumnName("dispute_note").HasMaxLength(1000);
        builder.Property(x => x.DisputedAt).HasColumnName("disputed_at");
        builder.Property(x => x.AdminAdjustmentNote).HasColumnName("admin_adjustment_note").HasMaxLength(500);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // Kapanış job'unun idempotency garantisi: aynı dönem+shop için ikinci satır asla
        // eklenemez, çift koşumda handler'ın "zaten var mı" ön-kontrolü atlanırsa bile.
        builder.HasIndex(x => new { x.BarberShopId, x.PeriodStart }).IsUnique().HasDatabaseName("ux_commission_statements_shop_period");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_commission_statements_status");
        builder.HasIndex(x => x.DueDate).HasDatabaseName("ix_commission_statements_due_date");

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class CommissionPaymentConfiguration : IEntityTypeConfiguration<CommissionPayment>
{
    public void Configure(EntityTypeBuilder<CommissionPayment> builder)
    {
        builder.ToTable("commission_payments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BarberShopId).HasColumnName("barber_shop_id");
        builder.Property(x => x.CommissionStatementId).HasColumnName("commission_statement_id");
        builder.Property(x => x.Provider).HasColumnName("provider").HasConversion<int>();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.AmountMinor).HasColumnName("amount_minor");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.ProviderTransactionId).HasColumnName("provider_transaction_id").HasMaxLength(120);
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(x => x.CapturedAt).HasColumnName("captured_at");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(64);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => x.CommissionStatementId).HasDatabaseName("ix_commission_payments_statement");
        builder.HasIndex(x => x.BarberShopId).HasDatabaseName("ix_commission_payments_shop");
        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("idempotency_key IS NOT NULL").HasDatabaseName("ux_commission_payments_idempotency");

        builder.Ignore(x => x.DomainEvents);
    }
}
