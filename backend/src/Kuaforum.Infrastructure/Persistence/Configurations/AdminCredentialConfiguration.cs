using Kuaforum.Domain.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class AdminCredentialConfiguration : IEntityTypeConfiguration<AdminCredential>
{
    public void Configure(EntityTypeBuilder<AdminCredential> builder)
    {
        builder.ToTable("admin_credentials");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id");
        builder.Property(a => a.UserId).HasColumnName("user_id");
        builder.Property(a => a.PasswordHash).HasColumnName("password_hash").HasMaxLength(500).IsRequired();
        builder.Property(a => a.TotpSecretEncrypted).HasColumnName("totp_secret_encrypted").HasMaxLength(500).IsRequired();
        builder.Property(a => a.LastPasswordChangeAt).HasColumnName("last_password_change_at");
        builder.Property(a => a.FailedLoginAttempts).HasColumnName("failed_login_attempts");
        builder.Property(a => a.LockedUntil).HasColumnName("locked_until");
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(a => a.UserId).IsUnique().HasDatabaseName("ux_admin_credentials_user_id");

        builder.Ignore(a => a.DomainEvents);
    }
}
