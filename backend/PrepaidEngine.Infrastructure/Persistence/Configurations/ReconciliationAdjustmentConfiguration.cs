using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class ReconciliationAdjustmentConfiguration : IEntityTypeConfiguration<ReconciliationAdjustment>
{
    public void Configure(EntityTypeBuilder<ReconciliationAdjustment> builder)
    {
        builder.ToTable("ReconciliationAdjustments");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.ConsumerNumber).IsRequired().HasMaxLength(30);
        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(r => r.PaymentMode).IsRequired().HasMaxLength(30);
        builder.Property(r => r.ReconciliationDate).IsRequired();
        builder.Property(r => r.Reference).IsRequired().HasMaxLength(500);
        builder.Property(r => r.BalanceAfter).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(r => r.AppliedAt).IsRequired();

        builder.HasIndex(r => r.ConsumerId);

        builder.HasOne<Consumer>()
            .WithMany()
            .HasForeignKey(r => r.ConsumerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
