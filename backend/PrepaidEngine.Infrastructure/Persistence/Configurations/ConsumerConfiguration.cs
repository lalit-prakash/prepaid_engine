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

        builder.HasOne(c => c.Dtr).WithMany().HasForeignKey(c => c.DtrId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(c => c.DtrId);

        builder.Property(c => c.Name)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(c => c.ServiceAddress)
            .HasMaxLength(500);

        builder.Property(c => c.ConnectionStatus)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(c => c.BillingMode)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.ConnectedLoadKw)
            .HasColumnType("decimal(18,3)")
            .IsRequired();

        builder.Property(c => c.IsNetMeter)
            .IsRequired();

        builder.Property(c => c.MobileNumber)
            .HasMaxLength(20);

        // Transformer/CT-PT/metering-side facts (TMC, CPMC, LT-side surcharge) — HT/EHT only, null/false for LT.
        builder.Property(c => c.SupplyVoltage).HasConversion<string>().HasMaxLength(10);
        builder.Property(c => c.MeteredOnLtSide).IsRequired();
        builder.Property(c => c.TransformerMaintenanceOptedIn).IsRequired();
        builder.Property(c => c.TransformerCapacityKva).HasColumnType("decimal(18,3)");
        builder.Property(c => c.CtPtMaintenanceOptedIn).IsRequired();
        builder.Property(c => c.CtPtWiring).HasConversion<string>().HasMaxLength(20);

        // Trigram indexes for the server-side search box (ILIKE prefix on account and mobile, contains on name).
        builder.HasIndex(c => c.AccountNumber, "IX_Consumers_AccountNumber_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
        builder.HasIndex(c => c.MobileNumber, "IX_Consumers_MobileNumber_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");
        builder.HasIndex(c => c.Name, "IX_Consumers_Name_trgm").HasMethod("gin").HasOperators("gin_trgm_ops");

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
