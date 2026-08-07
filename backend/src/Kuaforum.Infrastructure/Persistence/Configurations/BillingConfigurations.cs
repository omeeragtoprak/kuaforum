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
        builder.Property(x => x.CommissionPaymentIban).HasColumnName("commission_payment_iban").HasMaxLength(34);
        builder.Property(x => x.CommissionPaymentBankName).HasColumnName("commission_payment_bank_name").HasMaxLength(120);
        builder.Property(x => x.CommissionPaymentAccountHolderName).HasColumnName("commission_payment_account_holder_name").HasMaxLength(120);
        // HasDefaultValue şart — bkz. BarberConfigurations.cs'teki aynı gerekçe (aksi halde
        // mevcut PlatformSettings satırı bu migration'dan sonra min=max=0 alır ve owner hiçbir
        // DepositPercent'i geçerli aralığa sokamaz).
        builder.Property(x => x.DepositPercentMin).HasColumnName("deposit_percent_min").HasPrecision(5, 4).HasDefaultValue(0.05m);
        builder.Property(x => x.DepositPercentMax).HasColumnName("deposit_percent_max").HasPrecision(5, 4).HasDefaultValue(0.50m);
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.Ignore(x => x.DomainEvents);
    }
}

/// <summary>RFC-014 §2 D1: RFC-012'nin CommissionStatement'ının yerini alır — bkz. SubscriptionInvoice XML doc.</summary>
internal sealed class SubscriptionInvoiceConfiguration : IEntityTypeConfiguration<SubscriptionInvoice>
{
    public void Configure(EntityTypeBuilder<SubscriptionInvoice> builder)
    {
        builder.ToTable("subscription_invoices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BarberShopId).HasColumnName("barber_shop_id");
        builder.Property(x => x.PeriodStart).HasColumnName("period_start");
        builder.Property(x => x.PeriodEnd).HasColumnName("period_end");
        builder.Property(x => x.PlanCode).HasColumnName("plan_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.PlanPriceMinor).HasColumnName("plan_price_minor");
        builder.Property(x => x.AmountMinor).HasColumnName("amount_minor");
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
        builder.HasIndex(x => new { x.BarberShopId, x.PeriodStart }).IsUnique().HasDatabaseName("ux_subscription_invoices_shop_period");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_subscription_invoices_status");
        builder.HasIndex(x => x.DueDate).HasDatabaseName("ix_subscription_invoices_due_date");

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class SubscriptionPlanConfiguration : IEntityTypeConfiguration<SubscriptionPlan>
{
    public void Configure(EntityTypeBuilder<SubscriptionPlan> builder)
    {
        builder.ToTable("subscription_plans");
        // Code, doğal PK — bkz. SubscriptionPlan sınıfının XML doc gerekçesi.
        builder.HasKey(x => x.Code);
        builder.Property(x => x.Code).HasColumnName("code").HasMaxLength(32);
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(80);
        builder.Property(x => x.PriceMinor).HasColumnName("price_minor");
        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.DisplayOrder).HasColumnName("display_order");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");
    }
}

internal sealed class PlanEntitlementConfiguration : IEntityTypeConfiguration<PlanEntitlement>
{
    public void Configure(EntityTypeBuilder<PlanEntitlement> builder)
    {
        builder.ToTable("plan_entitlements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.PlanCode).HasColumnName("plan_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Key).HasColumnName("key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Value).HasColumnName("value").HasMaxLength(200).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // Seeder idempotency garantisi: aynı plan+key için ikinci satır asla eklenemez.
        builder.HasIndex(x => new { x.PlanCode, x.Key }).IsUnique().HasDatabaseName("ux_plan_entitlements_plan_key");
        builder.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => x.PlanCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Cascade);

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class ShopSubscriptionConfiguration : IEntityTypeConfiguration<ShopSubscription>
{
    public void Configure(EntityTypeBuilder<ShopSubscription> builder)
    {
        builder.ToTable("shop_subscriptions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BarberShopId).HasColumnName("barber_shop_id");
        builder.Property(x => x.PlanCode).HasColumnName("plan_code").HasMaxLength(32).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.CurrentPeriodStart).HasColumnName("current_period_start");
        builder.Property(x => x.CurrentPeriodEnd).HasColumnName("current_period_end");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // BarberShopId'ye EF-seviyesi FK bilinçli olarak eklenmedi — SubscriptionInvoice'ın
        // Barber modülüne aynı şekilde davrandığı desenle tutarlı (Billing modülü Barber'ın
        // internal entity'sini import etmez, yalnız Guid taşır).
        builder.HasIndex(x => x.BarberShopId).IsUnique().HasDatabaseName("ux_shop_subscriptions_shop");
        builder.HasOne<SubscriptionPlan>().WithMany().HasForeignKey(x => x.PlanCode).HasPrincipalKey(x => x.Code).OnDelete(DeleteBehavior.Restrict);

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class SubscriptionInvoicePaymentConfiguration : IEntityTypeConfiguration<SubscriptionInvoicePayment>
{
    public void Configure(EntityTypeBuilder<SubscriptionInvoicePayment> builder)
    {
        builder.ToTable("subscription_invoice_payments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BarberShopId).HasColumnName("barber_shop_id");
        builder.Property(x => x.SubscriptionInvoiceId).HasColumnName("subscription_invoice_id");
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

        builder.HasIndex(x => x.SubscriptionInvoiceId).HasDatabaseName("ix_subscription_invoice_payments_invoice");
        builder.HasIndex(x => x.BarberShopId).HasDatabaseName("ix_subscription_invoice_payments_shop");
        builder.HasIndex(x => x.IdempotencyKey).IsUnique().HasFilter("idempotency_key IS NOT NULL").HasDatabaseName("ux_subscription_invoice_payments_idempotency");

        builder.Ignore(x => x.DomainEvents);
    }
}
