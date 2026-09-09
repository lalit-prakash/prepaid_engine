using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class SmartMeterConfiguration : IEntityTypeConfiguration<SmartMeter>
{
    public void Configure(EntityTypeBuilder<SmartMeter> builder)
    {
        builder.ToTable("Meters");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.MeterNumber)
            .IsRequired()
            .HasMaxLength(50);
        builder.HasIndex(m => m.MeterNumber).IsUnique();

        builder.Property(m => m.LastReadingKwh)
            .HasColumnType("decimal(18,3)");
    }
}
