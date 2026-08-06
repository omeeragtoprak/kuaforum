using Kuaforum.Domain.Modules.Barber;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class BarberShopConfiguration : IEntityTypeConfiguration<BarberShop>
{
    public void Configure(EntityTypeBuilder<BarberShop> builder)
    {
        builder.ToTable("barber_shops");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.OwnerId).HasColumnName("owner_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Slug).HasColumnName("slug").HasMaxLength(120).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(2000);
        builder.Property(x => x.PhoneE164).HasColumnName("phone_e164").HasMaxLength(16);
        builder.Property(x => x.GenderType).HasColumnName("gender_type").HasConversion<int>();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.AddressCityId).HasColumnName("address_city_id");
        builder.Property(x => x.AddressDistrictId).HasColumnName("address_district_id");
        builder.Property(x => x.AddressNeighborhoodId).HasColumnName("address_neighborhood_id");
        builder.Property(x => x.AddressLine1).HasColumnName("address_line1").HasMaxLength(250);
        builder.Property(x => x.AddressLine2).HasColumnName("address_line2").HasMaxLength(250);
        builder.Property(x => x.AddressPostalCode).HasColumnName("address_postal_code").HasMaxLength(8);
        builder.Property(x => x.Latitude).HasColumnName("latitude");
        builder.Property(x => x.Longitude).HasColumnName("longitude");
        builder.Property(x => x.SubMerchantKey).HasColumnName("sub_merchant_key").HasMaxLength(120);
        builder.Property(x => x.CommissionTier).HasColumnName("commission_tier").HasMaxLength(20);
        builder.Property(x => x.CommissionRateOverride).HasColumnName("commission_rate_override").HasPrecision(5, 4);
        // HasDefaultValue şart: aksi halde EF migration'ı CLR sıfır değerini (false/0) SQL
        // DEFAULT'u yapar — C#'taki property initializer (`= 2`) yalnız `new BarberShop()`
        // anında uygulanır, mevcut satırlara AddColumn migration'ı ile eklenen kolonun DB
        // DEFAULT'una yansımaz. Bu olmadan mevcut tüm shop'lar MaxPendingPerCustomer=0 alır
        // ve InitiateBooking her müşteri için "limit doldu" der (kritik regresyon).
        builder.Property(x => x.BookingPolicyDepositEnabled).HasColumnName("booking_policy_deposit_enabled").HasDefaultValue(false);
        builder.Property(x => x.BookingPolicyDepositPercent).HasColumnName("booking_policy_deposit_percent").HasPrecision(5, 4);
        builder.Property(x => x.BookingPolicyMaxPendingPerCustomer).HasColumnName("booking_policy_max_pending_per_customer").HasDefaultValue(2);
        builder.Property(x => x.BookingPolicyMinDaysBetweenBookings).HasColumnName("booking_policy_min_days_between_bookings");
        builder.Property(x => x.BookingPolicyMaxBookingsPerMonth).HasColumnName("booking_policy_max_bookings_per_month");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // Tek-shop kuralı (V1): çift-submit yarışında handler'daki AnyAsync precheck'i
        // atlatılabilir — unique index son savunma hattı. V1.5 çoklu işletme gelirse düşürülür.
        builder.HasIndex(x => x.OwnerId).IsUnique().HasDatabaseName("ux_barber_shops_owner");
        builder.HasIndex(x => x.Slug).IsUnique().HasDatabaseName("ux_barber_shops_slug");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_barber_shops_status");
        builder.HasIndex(x => new { x.AddressCityId, x.AddressDistrictId }).HasDatabaseName("ix_barber_shops_city_district");
        builder.HasIndex(x => new { x.Latitude, x.Longitude }).HasDatabaseName("ix_barber_shops_lat_lng");

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class StylistConfiguration : IEntityTypeConfiguration<Stylist>
{
    public void Configure(EntityTypeBuilder<Stylist> builder)
    {
        builder.ToTable("stylists");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BarberShopId).HasColumnName("barber_shop_id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.PhotoUrl).HasColumnName("photo_url").HasMaxLength(500);
        builder.Property(x => x.Bio).HasColumnName("bio").HasMaxLength(1000);
        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.WalkInAvailable).HasColumnName("walk_in_available");
        builder.Property(x => x.WalkInToggledAt).HasColumnName("walk_in_toggled_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => x.BarberShopId).HasDatabaseName("ix_stylists_shop");
        builder.HasIndex(x => x.UserId).HasDatabaseName("ix_stylists_user");
        builder.HasIndex(x => new { x.BarberShopId, x.IsActive, x.WalkInAvailable }).HasDatabaseName("ix_stylists_shop_active_walkin");

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class ServiceCatalogConfiguration : IEntityTypeConfiguration<ServiceCatalog>
{
    public void Configure(EntityTypeBuilder<ServiceCatalog> builder)
    {
        builder.ToTable("service_catalogs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BarberShopId).HasColumnName("barber_shop_id");
        builder.Property(x => x.Name).HasColumnName("name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.Description).HasColumnName("description").HasMaxLength(500);
        builder.Property(x => x.Gender).HasColumnName("gender").HasConversion<int>();
        builder.Property(x => x.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(x => x.BufferMinutes).HasColumnName("buffer_minutes");
        builder.Property(x => x.PriceMinor).HasColumnName("price_minor");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3).IsRequired();
        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => new { x.BarberShopId, x.IsActive }).HasDatabaseName("ix_services_shop_active");

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class StylistServiceConfiguration : IEntityTypeConfiguration<StylistService>
{
    public void Configure(EntityTypeBuilder<StylistService> builder)
    {
        builder.ToTable("stylist_services");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.StylistId).HasColumnName("stylist_id");
        builder.Property(x => x.ServiceCatalogId).HasColumnName("service_catalog_id");
        builder.Property(x => x.OverrideDurationMinutes).HasColumnName("override_duration_minutes");
        builder.Property(x => x.OverridePriceMinor).HasColumnName("override_price_minor");
        builder.Property(x => x.IsActive).HasColumnName("is_active");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => new { x.StylistId, x.ServiceCatalogId }).IsUnique().HasDatabaseName("ux_stylist_services_pair");

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class WorkingHoursConfiguration : IEntityTypeConfiguration<WorkingHours>
{
    public void Configure(EntityTypeBuilder<WorkingHours> builder)
    {
        builder.ToTable("working_hours");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.StylistId).HasColumnName("stylist_id");
        builder.Property(x => x.DayOfWeek).HasColumnName("day_of_week").HasConversion<int>();
        builder.Property(x => x.OpenTime).HasColumnName("open_time");
        builder.Property(x => x.CloseTime).HasColumnName("close_time");
        builder.Property(x => x.IsClosed).HasColumnName("is_closed");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => new { x.StylistId, x.DayOfWeek }).IsUnique().HasDatabaseName("ux_working_hours_stylist_day");

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class TimeOffConfiguration : IEntityTypeConfiguration<TimeOff>
{
    public void Configure(EntityTypeBuilder<TimeOff> builder)
    {
        builder.ToTable("time_offs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.StylistId).HasColumnName("stylist_id");
        builder.Property(x => x.StartsAt).HasColumnName("starts_at");
        builder.Property(x => x.EndsAt).HasColumnName("ends_at");
        builder.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(250);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => new { x.StylistId, x.StartsAt, x.EndsAt }).HasDatabaseName("ix_time_offs_stylist_range");

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class KycSubmissionConfiguration : IEntityTypeConfiguration<KycSubmission>
{
    public void Configure(EntityTypeBuilder<KycSubmission> builder)
    {
        builder.ToTable("kyc_submissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BarberShopId).HasColumnName("barber_shop_id");
        builder.Property(x => x.SubmittedByUserId).HasColumnName("submitted_by_user_id");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasMaxLength(1000);
        builder.Property(x => x.SubmittedAt).HasColumnName("submitted_at");
        builder.Property(x => x.ReviewedAt).HasColumnName("reviewed_at");
        builder.Property(x => x.ReviewedByUserId).HasColumnName("reviewed_by_user_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => x.BarberShopId).HasDatabaseName("ix_kyc_submissions_shop");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_kyc_submissions_status");

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class KycDocumentConfiguration : IEntityTypeConfiguration<KycDocument>
{
    public void Configure(EntityTypeBuilder<KycDocument> builder)
    {
        builder.ToTable("kyc_documents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.SubmissionId).HasColumnName("submission_id");
        builder.Property(x => x.DocumentType).HasColumnName("document_type").HasConversion<int>();
        builder.Property(x => x.FileName).HasColumnName("file_name").HasMaxLength(250).IsRequired();
        builder.Property(x => x.StorageKey).HasColumnName("storage_key").HasMaxLength(500).IsRequired();
        builder.Property(x => x.SizeBytes).HasColumnName("size_bytes");
        builder.Property(x => x.ContentType).HasColumnName("content_type").HasMaxLength(100);
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => x.SubmissionId).HasDatabaseName("ix_kyc_documents_submission");

        builder.Ignore(x => x.DomainEvents);
    }
}
