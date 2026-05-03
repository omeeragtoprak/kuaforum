using Kuaforum.Domain.Modules.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class ReviewConfiguration : IEntityTypeConfiguration<Review>
{
    public void Configure(EntityTypeBuilder<Review> builder)
    {
        builder.ToTable("reviews");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.CustomerId).HasColumnName("customer_id");
        builder.Property(x => x.BarberShopId).HasColumnName("barber_shop_id");
        builder.Property(x => x.StylistId).HasColumnName("stylist_id");
        builder.Property(x => x.BookingId).HasColumnName("booking_id");
        builder.Property(x => x.Rating).HasColumnName("rating");
        builder.Property(x => x.Body).HasColumnName("body").HasMaxLength(1000);
        builder.Property(x => x.ModerationStatus).HasColumnName("moderation_status").HasConversion<int>();
        builder.Property(x => x.RejectionReason).HasColumnName("rejection_reason").HasConversion<int>();
        builder.Property(x => x.ModeratedAt).HasColumnName("moderated_at");
        builder.Property(x => x.ModeratedByUserId).HasColumnName("moderated_by_user_id");
        builder.Property(x => x.Version).HasColumnName("version").IsConcurrencyToken();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne(x => x.Reply).WithOne()
            .HasForeignKey<BarberReply>(r => r.ReviewId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => x.BookingId).IsUnique().HasDatabaseName("ux_reviews_booking");
        builder.HasIndex(x => x.BarberShopId).HasDatabaseName("ix_reviews_shop");
        builder.HasIndex(x => x.StylistId).HasDatabaseName("ix_reviews_stylist");
        builder.HasIndex(x => x.ModerationStatus).HasDatabaseName("ix_reviews_moderation");
        builder.HasIndex(x => new { x.BarberShopId, x.ModerationStatus, x.CreatedAt })
            .HasDatabaseName("ix_reviews_shop_status_created");

        builder.Ignore(x => x.DomainEvents);
    }
}

internal sealed class BarberReplyConfiguration : IEntityTypeConfiguration<BarberReply>
{
    public void Configure(EntityTypeBuilder<BarberReply> builder)
    {
        builder.ToTable("barber_replies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.ReviewId).HasColumnName("review_id");
        builder.Property(x => x.BarberOwnerUserId).HasColumnName("barber_owner_user_id");
        builder.Property(x => x.Body).HasColumnName("body").HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => x.ReviewId).IsUnique().HasDatabaseName("ux_replies_review");

        builder.Ignore(x => x.DomainEvents);
    }
}
