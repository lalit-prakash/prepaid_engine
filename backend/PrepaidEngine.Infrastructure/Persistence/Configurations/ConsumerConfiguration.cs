using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class ConsumerConfiguration : IEntityTypeConfiguration<Consumer>
{
    public void Configure(EntityTypeBuilder<Consumer> builder)
    {
        builder.ToTable("Consumers");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.AccountNumber)
            .IsRequired()
            .HasMaxLength(50);
        builder.HasIndex(c => c.AccountNumber).IsUnique();

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.ServiceAddress)
            .HasMaxLength(500);

        builder.Property(c => c.ConnectionStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        // One-to-one: a consumer is linked to a single smart meter.
        builder.HasOne(c => c.Meter)
            .WithOne()
            .HasForeignKey<Consumer>("MeterId")
            .OnDelete(DeleteBehavior.Restrict)
            .IsRequired();

        // One-to-one: the wallet is owned by (and cascades with) its consumer.
        builder.HasOne(c => c.Wallet)
            .WithOne()
            .HasForeignKey<PrepaidWallet>(w => w.ConsumerId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();
    }
}
