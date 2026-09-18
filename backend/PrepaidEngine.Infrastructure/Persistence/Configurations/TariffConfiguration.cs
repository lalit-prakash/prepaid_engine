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

        // Explicit default so a future migration that adds a similar column never repeats the
        // bug this one had to be hand-fixed for: EF's migration scaffolding does not infer a
        // sensible default string from a C# property initializer for a HasConversion<string>
        // enum, and silently emitted "" for existing rows without this.
        builder.Property(t => t.Status).HasConversion<string>().HasMaxLength(20).IsRequired()
            .HasDefaultValue(PrepaidEngine.Domain.Enums.TariffLifecycleStatus.Active);

        // Unique only among Active tariffs: a tariff-governance revision (see
        // TariffChangeRequest) creates a brand-new row with the same Name once the old one is
        // retired, so two rows sharing a Name is expected — the constraint that actually matters
        // is that only one of them is ever Active at a time.
        builder.HasIndex(t => t.Name).IsUnique().HasFilter("\"Status\" = 'Active'");

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

        // TouPeriod has its own identity (unlike TariffSlab) so it can be referenced/audited
        // independently if needed, but is still owned by Tariff — only IHT/IEHT tariffs
        // populate this collection; it's empty for everything else.
        builder.OwnsMany(t => t.TouPeriods, tou =>
        {
            tou.ToTable("TouPeriods");
            tou.WithOwner().HasForeignKey("TariffId");
            tou.HasKey(t => t.Id);

            tou.Property(t => t.Label).IsRequired().HasMaxLength(30);
            tou.Property(t => t.StartTime).IsRequired();
            tou.Property(t => t.EndTime).IsRequired();
            tou.Property(t => t.RatePerKvah).HasColumnType("decimal(18,4)").IsRequired();
        });

        builder.Navigation(t => t.TouPeriods)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
