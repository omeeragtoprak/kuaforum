using Kuaforum.Domain.Modules.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Kuaforum.Infrastructure.Persistence.Configurations;

internal sealed class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_logs");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").UseIdentityAlwaysColumn();
        builder.Property(a => a.ActorUserId).HasColumnName("actor_user_id");
        builder.Property(a => a.ActorType).HasColumnName("actor_type").HasConversion<int>();
        builder.Property(a => a.ActionType).HasColumnName("action_type").HasMaxLength(64).IsRequired();
        builder.Property(a => a.ResourceType).HasColumnName("resource_type").HasMaxLength(64).IsRequired();
        builder.Property(a => a.ResourceId).HasColumnName("resource_id").HasMaxLength(64).IsRequired();
        builder.Property(a => a.PayloadJson).HasColumnName("payload_json").HasColumnType("jsonb");
        builder.Property(a => a.Ip).HasColumnName("ip").HasMaxLength(45);
        builder.Property(a => a.UserAgent).HasColumnName("user_agent").HasMaxLength(500);
        builder.Property(a => a.RequestId).HasColumnName("request_id").HasMaxLength(36);
        builder.Property(a => a.CreatedAt).HasColumnName("created_at");

        builder.HasIndex(a => new { a.ActorUserId, a.CreatedAt }).HasDatabaseName("ix_audit_logs_actor_created");
        builder.HasIndex(a => new { a.ResourceType, a.ResourceId }).HasDatabaseName("ix_audit_logs_resource");
        builder.HasIndex(a => a.ActionType).HasDatabaseName("ix_audit_logs_action_type");
    }
}
