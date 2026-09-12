using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class MeterBillingControlConfiguration : IEntityTypeConfiguration<MeterBillingControl>
{
    public void Configure(EntityTypeBuilder<MeterBillingControl> builder)
    {
        builder.ToTable("MeterBillingControls");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.BlockReason).IsRequired().HasMaxLength(500);
        builder.Property(m => m.BlockedAt).IsRequired();

        // Unique per consumer + meter (spec §8).
        builder.HasIndex(m => new { m.ConsumerId, m.MeterId }).IsUnique();

        builder.HasOne<Consumer>().WithMany().HasForeignKey(m => m.ConsumerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SmartMeter>().WithMany().HasForeignKey(m => m.MeterId).OnDelete(DeleteBehavior.Restrict);
    }
}
