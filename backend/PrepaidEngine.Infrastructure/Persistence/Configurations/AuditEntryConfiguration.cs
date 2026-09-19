using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.EntityType).IsRequired().HasMaxLength(100);
        builder.Property(a => a.EntityId).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Action).IsRequired().HasMaxLength(100);
        builder.Property(a => a.Actor).IsRequired().HasMaxLength(200);
        builder.Property(a => a.OldValue).HasMaxLength(1000);
        builder.Property(a => a.NewValue).HasMaxLength(1000);
        builder.Property(a => a.Details).HasMaxLength(1000);
        builder.Property(a => a.OccurredAt).IsRequired();
        builder.Property(a => a.ActorRole).HasMaxLength(50);
        builder.Property(a => a.SourceIp).HasMaxLength(64);
        builder.Property(a => a.CorrelationId).HasMaxLength(64);

        builder.HasIndex(a => a.EntityType);
        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => a.CorrelationId);
    }
}
