using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class MeterCommandConfiguration : IEntityTypeConfiguration<MeterCommand>
{
    public void Configure(EntityTypeBuilder<MeterCommand> builder)
    {
        builder.ToTable("MeterCommands");
        builder.HasKey(m => m.Id);

        builder.Property(m => m.CreditAmount).HasColumnType("decimal(18,2)").IsRequired();

        builder.Property(m => m.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(m => m.RetryCount).IsRequired();
        builder.Property(m => m.ErrorMessage).HasMaxLength(500);
        builder.Property(m => m.ExternalCommandId).HasMaxLength(100);
        builder.Property(m => m.ResponseCode).HasMaxLength(50);
        builder.Property(m => m.ResponseMessage).HasMaxLength(500);

        builder.Property(m => m.CreatedAt).IsRequired();

        builder.HasIndex(m => m.ConsumerId);

        // Every meter credit command traces back to exactly one recharge — see MeterCommand's
        // doc comment. Unique, not just indexed: a recharge gets at most one credit command
        // (retries reuse the same command via Retry(), they don't create a new row).
        builder.HasIndex(m => m.RechargeTransactionId).IsUnique();

        builder.HasOne<Consumer>()
            .WithMany()
            .HasForeignKey(m => m.ConsumerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<RechargeTransaction>()
            .WithMany()
            .HasForeignKey(m => m.RechargeTransactionId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
