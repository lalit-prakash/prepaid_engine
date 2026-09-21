using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class DailyBillConfiguration : IEntityTypeConfiguration<DailyBill>
{
    public void Configure(EntityTypeBuilder<DailyBill> b)
    {
        b.ToTable("DailyBills");
        b.HasKey(x => x.Id);
        b.Property(x => x.Reference).IsRequired().HasMaxLength(80);
        b.HasIndex(x => x.Reference).IsUnique();
        b.HasIndex(x => new { x.ConsumerId, x.BillDate });
        foreach (var p in new[] { nameof(DailyBill.Kwh), nameof(DailyBill.MonthToDateKwhBefore) })
            b.Property(p).HasColumnType("decimal(18,3)");
        foreach (var p in new[]
        {
            nameof(DailyBill.GrossEnergyCharge), nameof(DailyBill.PrepaidRebate), nameof(DailyBill.FixedCharge), nameof(DailyBill.ElectricityDuty),
            nameof(DailyBill.LtSideMeteringSurcharge), nameof(DailyBill.Tmc), nameof(DailyBill.Cpmc), nameof(DailyBill.FppasShare),
        })
            b.Property(p).HasColumnType("decimal(18,4)");
        b.Property(x => x.BilledEnergy).HasColumnType("decimal(18,3)");
        b.Property(x => x.BilledUnit).IsRequired().HasMaxLength(5).HasDefaultValue("kWh");
        b.Property(x => x.Total).HasColumnType("decimal(18,2)");
        b.Property(x => x.Notes).HasMaxLength(600);
    }
}
