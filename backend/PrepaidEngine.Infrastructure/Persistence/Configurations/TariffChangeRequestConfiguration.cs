using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class TariffChangeRequestConfiguration : IEntityTypeConfiguration<TariffChangeRequest>
{
    public void Configure(EntityTypeBuilder<TariffChangeRequest> builder)
    {
        builder.ToTable("TariffChangeRequests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ProposedName).IsRequired().HasMaxLength(100);
        builder.Property(r => r.ProposedCategory).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(r => r.ProposedFixedChargePerUnitPerMonth).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(r => r.ProposedPrepaidEnergyRebatePercent).HasColumnType("decimal(5,2)").IsRequired();
        builder.Property(r => r.ProposedEmergencyCreditLimit).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(r => r.ProposedMinVendAmountSinglePhase).HasColumnType("decimal(18,2)");
        builder.Property(r => r.ProposedMaxVendAmountSinglePhase).HasColumnType("decimal(18,2)");
        builder.Property(r => r.ProposedMinVendAmountThreePhase).HasColumnType("decimal(18,2)");
        builder.Property(r => r.ProposedMaxVendAmountThreePhase).HasColumnType("decimal(18,2)");

        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.CreatedBy).IsRequired().HasMaxLength(100);
        builder.Property(r => r.ChangeReason).HasMaxLength(1000);
        builder.Property(r => r.SubmittedBy).HasMaxLength(100);
        builder.Property(r => r.ApprovedBy).HasMaxLength(100);
        builder.Property(r => r.RejectedBy).HasMaxLength(100);
        builder.Property(r => r.RejectionReason).HasMaxLength(1000);

        builder.HasIndex(r => r.SupersedesTariffId);
        builder.HasIndex(r => r.Status);

        // Same owned-value-object pattern as TariffConfiguration — proposed slabs/ToD periods
        // reuse TariffSlab/TouPeriod directly rather than a parallel snapshot type.
        builder.OwnsMany(r => r.ProposedSlabs, slab =>
        {
            slab.ToTable("TariffChangeRequestSlabs");
            slab.WithOwner().HasForeignKey("TariffChangeRequestId");
            slab.HasKey(s => s.Id);

            slab.Property(s => s.FromKwh).HasColumnType("decimal(18,3)").IsRequired();
            slab.Property(s => s.UpToKwh).HasColumnType("decimal(18,3)");
            slab.Property(s => s.RatePerKwh).HasColumnType("decimal(18,4)").IsRequired();
        });
        builder.Navigation(r => r.ProposedSlabs).UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.OwnsMany(r => r.ProposedTouPeriods, tou =>
        {
            tou.ToTable("TariffChangeRequestTouPeriods");
            tou.WithOwner().HasForeignKey("TariffChangeRequestId");
            tou.HasKey(t => t.Id);

            tou.Property(t => t.Label).IsRequired().HasMaxLength(30);
            tou.Property(t => t.StartTime).IsRequired();
            tou.Property(t => t.EndTime).IsRequired();
            tou.Property(t => t.RatePerKvah).HasColumnType("decimal(18,4)").IsRequired();
        });
        builder.Navigation(r => r.ProposedTouPeriods).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
