using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class ReverseConversionRequestConfiguration : IEntityTypeConfiguration<ReverseConversionRequest>
{
    public void Configure(EntityTypeBuilder<ReverseConversionRequest> builder)
    {
        builder.ToTable("ReverseConversionRequests");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RequestedBy).IsRequired().HasMaxLength(100);
        builder.Property(r => r.Reason).IsRequired().HasMaxLength(500);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(r => r.DecisionNote).HasMaxLength(500);
        builder.Property(r => r.FinalMeterReadingKwh).HasColumnType("decimal(18,3)");
        builder.Property(r => r.FinalWalletBalance).HasColumnType("decimal(18,2)");
        builder.Property(r => r.RequestedAt).IsRequired();

        builder.HasIndex(r => r.ConsumerId);

        builder.HasOne<Consumer>().WithMany().HasForeignKey(r => r.ConsumerId).OnDelete(DeleteBehavior.Restrict);
    }
}
