using Kuaforum.Domain.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class OtpCodeConfiguration : IEntityTypeConfiguration<OtpCode>
{
    public void Configure(EntityTypeBuilder<OtpCode> builder)
    {
        builder.ToTable("otp_codes");

        builder.HasKey(o => o.Id);
        builder.Property(o => o.Id).HasColumnName("id");
        builder.Property(o => o.PhoneE164).HasColumnName("phone_e164").HasMaxLength(16).IsRequired();
        builder.Property(o => o.CodeHash).HasColumnName("code_hash").HasMaxLength(128).IsRequired();
        builder.Property(o => o.Purpose).HasColumnName("purpose").HasConversion<int>();
        builder.Property(o => o.ExpiresAt).HasColumnName("expires_at");
        builder.Property(o => o.AttemptsLeft).HasColumnName("attempts_left");
        builder.Property(o => o.Status).HasColumnName("status").HasConversion<int>();
        builder.Property(o => o.UsedAt).HasColumnName("used_at");
        builder.Property(o => o.CreatedAt).HasColumnName("created_at");
        builder.Property(o => o.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(o => new { o.PhoneE164, o.Status }).HasDatabaseName("ix_otp_codes_phone_status");
        builder.HasIndex(o => o.ExpiresAt).HasDatabaseName("ix_otp_codes_expires_at");

        builder.Ignore(o => o.DomainEvents);
        builder.Ignore(o => o.IsActive);
    }
}
