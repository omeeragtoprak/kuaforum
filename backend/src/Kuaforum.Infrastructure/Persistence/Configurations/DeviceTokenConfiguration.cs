using Kuaforum.Domain.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> builder)
    {
        builder.ToTable("device_tokens");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).HasColumnName("id");
        builder.Property(d => d.UserId).HasColumnName("user_id");
        builder.Property(d => d.FcmToken).HasColumnName("fcm_token").HasMaxLength(512).IsRequired();
        builder.Property(d => d.Platform).HasColumnName("platform").HasConversion<int>();
        builder.Property(d => d.AppVersion).HasColumnName("app_version").HasMaxLength(32);
        builder.Property(d => d.DeviceModel).HasColumnName("device_model").HasMaxLength(64);
        builder.Property(d => d.LastSeenAt).HasColumnName("last_seen_at");
        builder.Property(d => d.IsActive).HasColumnName("is_active").HasDefaultValue(true);
        builder.Property(d => d.CreatedAt).HasColumnName("created_at");
        builder.Property(d => d.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(d => d.UserId).HasDatabaseName("ix_device_tokens_user_id");
        builder.HasIndex(d => d.FcmToken).IsUnique().HasDatabaseName("ux_device_tokens_fcm_token");
        builder.HasIndex(d => new { d.UserId, d.IsActive }).HasDatabaseName("ix_device_tokens_user_active");

        builder.Ignore(d => d.DomainEvents);
    }
}
