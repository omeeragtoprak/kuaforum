using Kuaforum.Domain.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id");
        builder.Property(r => r.UserId).HasColumnName("user_id");
        builder.Property(r => r.TokenHash).HasColumnName("token_hash").HasMaxLength(128).IsRequired();
        builder.Property(r => r.DeviceFingerprint).HasColumnName("device_fingerprint").HasMaxLength(64).IsRequired();
        builder.Property(r => r.DeviceName).HasColumnName("device_name").HasMaxLength(100);
        builder.Property(r => r.ExpiresAt).HasColumnName("expires_at");
        builder.Property(r => r.RevokedAt).HasColumnName("revoked_at");
        builder.Property(r => r.RevokedReason).HasColumnName("revoked_reason").HasConversion<int>();
        builder.Property(r => r.ReplacedByTokenId).HasColumnName("replaced_by_token_id");
        builder.Property(r => r.LastUsedAt).HasColumnName("last_used_at");
        builder.Property(r => r.LastUsedIp).HasColumnName("last_used_ip").HasMaxLength(45);
        builder.Property(r => r.CreatedAt).HasColumnName("created_at");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(r => r.UserId).HasDatabaseName("ix_refresh_tokens_user_id");
        builder.HasIndex(r => r.TokenHash).IsUnique().HasDatabaseName("ux_refresh_tokens_token_hash");
        builder.HasIndex(r => new { r.UserId, r.DeviceFingerprint }).HasDatabaseName("ix_refresh_tokens_user_device");
        builder.HasIndex(r => r.ExpiresAt).HasDatabaseName("ix_refresh_tokens_expires_at");

        builder.Ignore(r => r.DomainEvents);
        builder.Ignore(r => r.IsActive);
        builder.Ignore(r => r.IsRevoked);
        builder.Ignore(r => r.IsExpired);
    }
}
