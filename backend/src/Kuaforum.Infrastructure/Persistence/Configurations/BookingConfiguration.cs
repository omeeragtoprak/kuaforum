using Kuaforum.Domain.Modules.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        builder.ToTable("bookings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.BarberShopId).HasColumnName("barber_shop_id");
        builder.Property(x => x.StylistId).HasColumnName("stylist_id");
        builder.Property(x => x.ScheduledAt).HasColumnName("scheduled_at");
        builder.Property(x => x.ScheduledEndsAt).HasColumnName("scheduled_ends_at");
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.BookingType).HasColumnName("booking_type").HasConversion<int>();
        builder.Property(x => x.TotalAmountMinor).HasColumnName("total_amount_minor");
        builder.Property(x => x.PlatformCommissionMinor).HasColumnName("platform_commission_minor");
        builder.Property(x => x.Currency).HasColumnName("currency").HasMaxLength(3);
        builder.Property(x => x.CustomerNote).HasColumnName("customer_note").HasMaxLength(500);
        builder.Property(x => x.ConfirmedAt).HasColumnName("confirmed_at");
        builder.Property(x => x.StartedAt).HasColumnName("started_at");
        builder.Property(x => x.CompletedAt).HasColumnName("completed_at");
        builder.Property(x => x.CancelledAt).HasColumnName("cancelled_at");
        builder.Property(x => x.CancellationReason).HasColumnName("cancellation_reason").HasConversion<int>();
        builder.Property(x => x.RefundPercentage).HasColumnName("refund_percentage");
        builder.Property(x => x.PaymentId).HasColumnName("payment_id");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasMany(x => x.Items).WithOne()
            .HasForeignKey(i => i.BookingId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_bookings_customer");
        builder.HasIndex(x => x.StylistId).HasDatabaseName("ix_bookings_stylist");
        builder.HasIndex(x => x.BarberShopId).HasDatabaseName("ix_bookings_shop");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_bookings_status");
        builder.HasIndex(x => new { x.StylistId, x.ScheduledAt, x.ScheduledEndsAt }).HasDatabaseName("ix_bookings_stylist_range");

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class BookingItemConfiguration : IEntityTypeConfiguration<BookingItem>
{
    public void Configure(EntityTypeBuilder<BookingItem> builder)
    {
        builder.ToTable("booking_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BookingId).HasColumnName("booking_id");
        builder.Property(x => x.ServiceCatalogId).HasColumnName("service_catalog_id");
        builder.Property(x => x.ServiceName).HasColumnName("service_name").HasMaxLength(80).IsRequired();
        builder.Property(x => x.DurationMinutes).HasColumnName("duration_minutes");
        builder.Property(x => x.BufferMinutes).HasColumnName("buffer_minutes");
        builder.Property(x => x.PriceMinor).HasColumnName("price_minor");
        builder.Property(x => x.OrderIndex).HasColumnName("order_index");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => x.BookingId).HasDatabaseName("ix_booking_items_booking");

        builder.Ignore(x => x.DomainEvents);
    }
}
