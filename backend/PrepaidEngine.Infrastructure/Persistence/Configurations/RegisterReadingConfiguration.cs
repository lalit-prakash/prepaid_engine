using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class RegisterReadingConfiguration : IEntityTypeConfiguration<RegisterReading>
{
    public void Configure(EntityTypeBuilder<RegisterReading> builder)
    {
        builder.ToTable("RegisterReadings");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ReadingTimestamp).IsRequired();
        builder.Property(r => r.CumulativeImportKwh).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.ReceivedAt).IsRequired();
        builder.Property(r => r.SourceReference).HasMaxLength(100);

        builder.HasIndex(r => new { r.ConsumerId, r.MeterId, r.ReadingTimestamp }).IsUnique();
        builder.HasIndex(r => r.MeterId);

        builder.HasOne<Consumer>().WithMany().HasForeignKey(r => r.ConsumerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SmartMeter>().WithMany().HasForeignKey(r => r.MeterId).OnDelete(DeleteBehavior.Restrict);
    }
}
