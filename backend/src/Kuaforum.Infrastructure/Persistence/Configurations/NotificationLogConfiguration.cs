using Kuaforum.Domain.Modules.Notification;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class NotificationLogConfiguration : IEntityTypeConfiguration<NotificationLog>
{
    public void Configure(EntityTypeBuilder<NotificationLog> builder)
    {
        builder.ToTable("notification_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id");
        builder.Property(x => x.UserId).HasColumnName("user_id");
        builder.Property(x => x.Channel).HasColumnName("channel").HasConversion<int>();
        builder.Property(x => x.TemplateKey).HasColumnName("template_key").HasMaxLength(64).IsRequired();
        builder.Property(x => x.Recipient).HasColumnName("recipient").HasMaxLength(256).IsRequired();
        builder.Property(x => x.Subject).HasColumnName("subject").HasMaxLength(200).IsRequired();
        builder.Property(x => x.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
        builder.Property(x => x.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(x => x.FailureReason).HasColumnName("failure_reason").HasMaxLength(500);
        builder.Property(x => x.ProviderMessageId).HasColumnName("provider_message_id").HasMaxLength(120);
        builder.Property(x => x.SentAt).HasColumnName("sent_at");
        builder.Property(x => x.FailedAt).HasColumnName("failed_at");
        builder.Property(x => x.IsRead).HasColumnName("is_read").HasDefaultValue(false);
        builder.Property(x => x.ReadAt).HasColumnName("read_at");
        builder.Property(x => x.RelatedBookingId).HasColumnName("related_booking_id");
        builder.Property(x => x.CreatedAt).HasColumnName("created_at");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(x => new { x.UserId, x.CreatedAt }).HasDatabaseName("ix_notification_logs_user_created");
        builder.HasIndex(x => x.Status).HasDatabaseName("ix_notification_logs_status");
        builder.HasIndex(x => x.TemplateKey).HasDatabaseName("ix_notification_logs_template");
        builder.HasIndex(x => new { x.UserId, x.Channel, x.IsRead }).HasDatabaseName("ix_notification_logs_user_channel_read");

        builder.Ignore(x => x.DomainEvents);
    }
}
