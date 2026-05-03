using Kuaforum.Domain.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class ConsentConfiguration : IEntityTypeConfiguration<Consent>
{
    public void Configure(EntityTypeBuilder<Consent> builder)
    {
        builder.ToTable("consents");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id");
        builder.Property(c => c.UserId).HasColumnName("user_id");
        builder.Property(c => c.Purpose).HasColumnName("purpose").HasConversion<int>();
        builder.Property(c => c.Granted).HasColumnName("granted");
        builder.Property(c => c.Version).HasColumnName("version").HasMaxLength(20).IsRequired();
        builder.Property(c => c.GrantedAt).HasColumnName("granted_at");
        builder.Property(c => c.RevokedAt).HasColumnName("revoked_at");
        builder.Property(c => c.Source).HasColumnName("source").HasConversion<int>();
        builder.Property(c => c.CreatedAt).HasColumnName("created_at");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at");

        builder.HasIndex(c => new { c.UserId, c.Purpose, c.GrantedAt }).HasDatabaseName("ix_consents_user_purpose_granted");

        builder.Ignore(c => c.DomainEvents);
    }
}
