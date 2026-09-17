using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class MeterEventConfiguration : IEntityTypeConfiguration<MeterEvent>
{
    public void Configure(EntityTypeBuilder<MeterEvent> builder)
    {
        builder.ToTable("MeterEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.EventCode).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(e => e.EventTimestamp).IsRequired();
        builder.Property(e => e.Description).HasMaxLength(500);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.ReceivedAt).IsRequired();
        builder.Property(e => e.SourceReference).HasMaxLength(100);

        builder.HasIndex(e => new { e.MeterId, e.EventCode, e.EventTimestamp }).IsUnique();
        builder.HasIndex(e => e.ConsumerId);

        builder.HasOne<Consumer>().WithMany().HasForeignKey(e => e.ConsumerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SmartMeter>().WithMany().HasForeignKey(e => e.MeterId).OnDelete(DeleteBehavior.Restrict);
    }
}
