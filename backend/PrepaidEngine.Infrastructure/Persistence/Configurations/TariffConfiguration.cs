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
