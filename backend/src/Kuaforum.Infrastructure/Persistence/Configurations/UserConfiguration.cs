using Kuaforum.Domain.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");

        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).HasColumnName("id");

        builder.Property(u => u.PhoneE164).HasColumnName("phone_e164").HasMaxLength(16);
        builder.Property(u => u.Email).HasColumnName("email").HasMaxLength(256);
        builder.Property(u => u.EmailNormalized).HasColumnName("email_normalized").HasMaxLength(256);
        builder.Property(u => u.DisplayName).HasColumnName("display_name").HasMaxLength(80);
        builder.Property(u => u.AvatarUrl).HasColumnName("avatar_url").HasMaxLength(500);

        builder.Property(u => u.Role).HasColumnName("role").HasConversion<int>();
        builder.Property(u => u.Status).HasColumnName("status").HasConversion<int>();

        builder.Property(u => u.CreatedAt).HasColumnName("created_at");
        builder.Property(u => u.UpdatedAt).HasColumnName("updated_at");
        builder.Property(u => u.DeletedAt).HasColumnName("deleted_at");
        builder.Property(u => u.LastLoginAt).HasColumnName("last_login_at");

        builder.Property(u => u.Locale).HasColumnName("locale").HasMaxLength(8).IsRequired();
        builder.Property(u => u.TimeZone).HasColumnName("time_zone").HasMaxLength(64).IsRequired();

        builder.Property(u => u.Version).HasColumnName("version").IsConcurrencyToken();

        builder.HasIndex(u => u.PhoneE164).IsUnique().HasFilter("phone_e164 IS NOT NULL").HasDatabaseName("ux_users_phone");
        builder.HasIndex(u => u.EmailNormalized).IsUnique().HasFilter("email_normalized IS NOT NULL").HasDatabaseName("ux_users_email_normalized");
        builder.HasIndex(u => u.Role).HasDatabaseName("ix_users_role");
        builder.HasIndex(u => u.DeletedAt).HasDatabaseName("ix_users_deleted_at");

        builder.Ignore(u => u.DomainEvents);
        builder.Ignore(u => u.IsDeleted);
        builder.Ignore(u => u.CanLogin);
    }
}
