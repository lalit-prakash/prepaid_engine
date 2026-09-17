using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class InstantaneousReadingConfiguration : IEntityTypeConfiguration<InstantaneousReading>
{
    public void Configure(EntityTypeBuilder<InstantaneousReading> builder)
    {
        builder.ToTable("InstantaneousReadings");
        builder.HasKey(i => i.Id);

        builder.Property(i => i.Timestamp).IsRequired();
        builder.Property(i => i.VoltageVolts).HasColumnType("decimal(9,2)").IsRequired();
        builder.Property(i => i.CurrentAmps).HasColumnType("decimal(9,2)").IsRequired();
        builder.Property(i => i.PowerKw).HasColumnType("decimal(9,3)").IsRequired();
        builder.Property(i => i.PowerFactor).HasColumnType("decimal(4,3)").IsRequired();
        builder.Property(i => i.FrequencyHz).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(i => i.RelayStatus).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(i => i.ReceivedAt).IsRequired();
        builder.Property(i => i.SourceReference).HasMaxLength(100);

        builder.HasIndex(i => new { i.MeterId, i.Timestamp }).IsUnique();
        builder.HasIndex(i => i.ConsumerId);

        builder.HasOne<Consumer>().WithMany().HasForeignKey(i => i.ConsumerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SmartMeter>().WithMany().HasForeignKey(i => i.MeterId).OnDelete(DeleteBehavior.Restrict);
    }
}
