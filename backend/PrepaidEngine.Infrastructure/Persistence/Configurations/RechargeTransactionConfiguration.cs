using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class RechargeTransactionConfiguration : IEntityTypeConfiguration<RechargeTransaction>
{
    public void Configure(EntityTypeBuilder<RechargeTransaction> builder)
    {
        builder.ToTable("RechargeTransactions");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Amount).HasColumnType("decimal(18,2)").IsRequired();

        builder.Property(r => r.RmsReferenceId)
            .IsRequired()
            .HasMaxLength(100);
        // The RMS reference is the idempotency key for recharge processing: RMS is the
        // authoritative wallet/payment system, and this table only tracks the Prepaid
        // Engine's view of that recharge, so duplicate callbacks must not create duplicates.
        builder.HasIndex(r => r.RmsReferenceId).IsUnique();

        builder.Property(r => r.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(r => r.InitiatedAt).IsRequired();

        builder.HasIndex(r => r.ConsumerId);

        builder.HasOne<Consumer>()
            .WithMany()
            .HasForeignKey(r => r.ConsumerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
