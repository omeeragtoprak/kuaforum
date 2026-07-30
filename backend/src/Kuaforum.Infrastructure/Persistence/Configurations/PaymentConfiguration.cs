using Kuaforum.Domain.Modules.Payment;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BookingId).HasColumnName("booking_id");
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.SubMerchantBarberShopId).HasColumnName("sub_merchant_barber_shop_id");
        builder.Property(x => x.Provider).HasColumnName("provider").HasConversion<int>();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.Kind).HasColumnName("kind").HasConversion<int>().HasDefaultValue(PaymentKind.FullOnline);
        builder.Property(x => x.GrossAmountMinor).HasColumnName("gross_amount_minor");
        builder.Property(x => x.PlatformCommissionMinor).HasColumnName("platform_commission_minor");
        builder.Property(x => x.PspMdrMinor).HasColumnName("psp_mdr_minor");
        builder.Property(x => x.NetToBarberMinor).HasColumnName("net_to_barber_minor");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.ProviderTransactionId).HasColumnName("provider_transaction_id").HasMaxLength(120);
        builder.Property(x => x.ProviderPaymentToken).HasColumnName("provider_payment_token").HasMaxLength(120);
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(x => x.AuthorizedAt).HasColumnName("authorized_at");
        builder.Property(x => x.CapturedAt).HasColumnName("captured_at");
        builder.Property(x => x.SettledAt).HasColumnName("settled_at");
        builder.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key").HasMaxLength(64);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasMany(x => x.Refunds).WithOne()
            .HasForeignKey(r => r.PaymentId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.BookingId).IsUnique().HasDatabaseName("ux_payments_booking");
        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_payments_customer");
        builder.HasIndex(x => new { x.CustomerId, x.CreatedAt }).HasDatabaseName("ix_payments_customer_created");
        builder.HasIndex(x => x.SubMerchantBarberShopId).HasDatabaseName("ix_payments_shop");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_payments_status");
        builder.HasIndex(x => x.ProviderTransactionId).HasDatabaseName("ix_payments_provider_tx");
        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("idempotency_key IS NOT NULL").HasDatabaseName("ux_payments_idempotency");

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class RefundConfiguration : IEntityTypeConfiguration<Refund>
{
    public void Configure(EntityTypeBuilder<Refund> builder)
    {
        builder.ToTable("refunds");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.PaymentId).HasColumnName("payment_id");
        builder.Property(x => x.AmountMinor).HasColumnName("amount_minor");
        builder.Property(x => x.Reason).HasColumnName("reason").HasConversion<int>();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.ProviderRefundId).HasColumnName("provider_refund_id").HasMaxLength(120);
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => x.PaymentId).HasDatabaseName("ix_refunds_payment");
        builder.HasIndex(x => x.ProviderRefundId).HasDatabaseName("ix_refunds_provider");

        builder.Ignore(x => x.DomainEvents);
    }
}
