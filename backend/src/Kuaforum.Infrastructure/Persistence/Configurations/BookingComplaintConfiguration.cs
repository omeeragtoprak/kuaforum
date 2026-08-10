using Kuaforum.Domain.Modules.Booking;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class BookingComplaintConfiguration : IEntityTypeConfiguration<BookingComplaint>
{
    public void Configure(EntityTypeBuilder<BookingComplaint> builder)
    {
        builder.ToTable("booking_complaints");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.BookingId).HasColumnName("booking_id");
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.BarberShopId).HasColumnName("barber_shop_id");
        builder.Property(x => x.Note).HasColumnName("note").HasMaxLength(1000);
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.ReportedAt).HasColumnName("reported_at");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        // RFC-015 §2 D3 madde 2: "randevu başına tek bildirim" — application-layer no-op guard
        // (FileBookingComplaintCommandHandler) bunu zaten sağlıyor ama unique index ikinci bir
        // güvence katmanı: eşzamanlı çift-tıklama/retry senaryosunda DB seviyesinde de garanti.
        builder.HasIndex(x => x.BookingId).IsUnique().HasDatabaseName("ux_booking_complaints_booking");
        builder.HasIndex(x => x.CustomerId).HasDatabaseName("ix_booking_complaints_customer");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_booking_complaints_status");
        builder.HasIndex(x => x.BarberShopId).HasDatabaseName("ix_booking_complaints_shop");

        builder.Ignore(x => x.DomainEvents);
    }
}
