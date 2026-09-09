using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class ConsumptionReadingConfiguration : IEntityTypeConfiguration<ConsumptionReading>
{
    public void Configure(EntityTypeBuilder<ConsumptionReading> builder)
    {
        builder.ToTable("ConsumptionReadings");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ConsumptionKwh)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(r => r.PeriodStart).IsRequired();
        builder.Property(r => r.PeriodEnd).IsRequired();

        builder.HasIndex(r => r.ConsumerId);

        builder.HasOne<Consumer>()
            .WithMany()
            .HasForeignKey(r => r.ConsumerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
