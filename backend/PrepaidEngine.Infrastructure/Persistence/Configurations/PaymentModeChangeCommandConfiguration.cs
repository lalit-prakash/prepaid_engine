using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class PaymentModeChangeCommandConfiguration : IEntityTypeConfiguration<PaymentModeChangeCommand>
{
    public void Configure(EntityTypeBuilder<PaymentModeChangeCommand> builder)
    {
        builder.ToTable("PaymentModeChangeCommands");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.ErrorMessage).HasMaxLength(500);
        builder.Property(c => c.MeterReadingAtConversion).HasColumnType("decimal(18,3)");
        builder.Property(c => c.CreatedAt).IsRequired();

        // At most one payment-mode-change command per conversion request — the same 1:1 shape
        // MeterCommand uses for RechargeTransactionId.
        builder.HasIndex(c => c.ConversionRequestId).IsUnique();
        builder.HasIndex(c => c.ConsumerId);

        builder.HasOne<ConversionRequest>()
            .WithMany()
            .HasForeignKey(c => c.ConversionRequestId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Consumer>()
            .WithMany()
            .HasForeignKey(c => c.ConsumerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
