using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class MeterAssignmentConfiguration : IEntityTypeConfiguration<MeterAssignment>
{
    public void Configure(EntityTypeBuilder<MeterAssignment> builder)
    {
        builder.ToTable("MeterAssignments");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.EventType).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(m => m.EffectiveFrom).IsRequired();
        builder.Property(m => m.OldMeterClosingReadingKwh).HasColumnType("decimal(18,3)");
        builder.Property(m => m.NewMeterOpeningReadingKwh).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(m => m.Reason).HasMaxLength(500);
        builder.Property(m => m.RecordedAt).IsRequired();

        builder.HasIndex(m => m.ConsumerId);

        builder.HasOne<Consumer>().WithMany().HasForeignKey(m => m.ConsumerId).OnDelete(DeleteBehavior.Restrict);
    }
}
