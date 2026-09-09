using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class TariffConfiguration : IEntityTypeConfiguration<Tariff>
{
    public void Configure(EntityTypeBuilder<Tariff> builder)
    {
        builder.ToTable("Tariffs");
        builder.HasKey(t => t.Id);

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(100);
        builder.HasIndex(t => t.Name).IsUnique();

        builder.Property(t => t.Category)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(t => t.FixedChargePerUnitPerMonth).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(t => t.PrepaidEnergyRebatePercent).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(t => t.EmergencyCreditLimit).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(t => t.MinVendAmountSinglePhase).HasColumnType("decimal(18,2)");
        builder.Property(t => t.MaxVendAmountSinglePhase).HasColumnType("decimal(18,2)");
        builder.Property(t => t.MinVendAmountThreePhase).HasColumnType("decimal(18,2)");
        builder.Property(t => t.MaxVendAmountThreePhase).HasColumnType("decimal(18,2)");

        // TariffSlab has no identity of its own; it is a value object owned by Tariff and
        // persisted in its own table keyed by (TariffId, a shadow slab index).
        builder.OwnsMany(t => t.Slabs, slab =>
        {
            slab.ToTable("TariffSlabs");
            slab.WithOwner().HasForeignKey("TariffId");
            slab.HasKey(s => s.Id);

            slab.Property(s => s.FromKwh).HasColumnType("decimal(18,3)").IsRequired();
            slab.Property(s => s.UpToKwh).HasColumnType("decimal(18,3)");
            slab.Property(s => s.RatePerKwh).HasColumnType("decimal(18,4)").IsRequired();
        });

        builder.Navigation(t => t.Slabs)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
