using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class EnergyValidationResultConfiguration : IEntityTypeConfiguration<EnergyValidationResult>
{
    public void Configure(EntityTypeBuilder<EnergyValidationResult> builder)
    {
        builder.ToTable("EnergyValidationResults");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.ValidationDate).IsRequired();
        builder.Property(v => v.Rule).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(v => v.ExpectedValueKwh).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(v => v.ActualValueKwh).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(v => v.VarianceKwh).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(v => v.VariancePct).HasColumnType("decimal(9,2)").IsRequired();
        builder.Property(v => v.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(v => v.Reason).IsRequired().HasMaxLength(500);
        builder.Property(v => v.EvaluatedAt).IsRequired();

        builder.HasIndex(v => new { v.ConsumerId, v.MeterId, v.ValidationDate, v.Rule }).IsUnique();
        builder.HasIndex(v => v.Status);

        builder.HasOne<Consumer>().WithMany().HasForeignKey(v => v.ConsumerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SmartMeter>().WithMany().HasForeignKey(v => v.MeterId).OnDelete(DeleteBehavior.Restrict);
    }
}
