using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class PrepaidBillConfiguration : IEntityTypeConfiguration<PrepaidBill>
{
    public void Configure(EntityTypeBuilder<PrepaidBill> builder)
    {
        builder.ToTable("PrepaidBills");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.EnergyChargeGross).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.PrepaidRebateAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.FixedCharge).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.ElectricityDutyAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.FppasAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.TmcAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.CpmcAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.ArrearsAmount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.ArrearsRecovered).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.AmountPaid).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.GeneratedAt).IsRequired();

        // EnergyChargeNet is a computed property (EnergyChargeGross - PrepaidRebateAmount), not a column.
        builder.Ignore(b => b.EnergyChargeNet);

        builder.Property(b => b.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(b => b.ConsumerId);
        builder.HasIndex(b => b.ConsumptionReadingId).IsUnique();
        builder.HasIndex(b => b.FppasChargeId);

        builder.HasOne<Consumer>()
            .WithMany()
            .HasForeignKey(b => b.ConsumerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ConsumptionReading>()
            .WithMany()
            .HasForeignKey(b => b.ConsumptionReadingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Tariff>()
            .WithMany()
            .HasForeignKey(b => b.TariffId)
            .OnDelete(DeleteBehavior.Restrict);

        // A bill's FPPAS share links back to the notification it was allocated from; nullable
        // since most bills have no FPPAS applicable that period.
        builder.HasOne<FppasCharge>()
            .WithMany()
            .HasForeignKey(b => b.FppasChargeId)
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired(false);
    }
}
