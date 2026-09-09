using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class FppasChargeConfiguration : IEntityTypeConfiguration<FppasCharge>
{
    public void Configure(EntityTypeBuilder<FppasCharge> builder)
    {
        builder.ToTable("FppasCharges");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.SourceEnergyCharge).HasColumnType("decimal(18,2)").IsRequired();
        // 4 decimal places: rates like 6.65% (0.0665) need more precision than money itself.
        builder.Property(f => f.RateFraction).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(f => f.NotifiedAt).IsRequired();

        // TotalAmount is a computed property (SourceEnergyCharge * RateFraction), not a column.
        builder.Ignore(f => f.TotalAmount);
    }
}
