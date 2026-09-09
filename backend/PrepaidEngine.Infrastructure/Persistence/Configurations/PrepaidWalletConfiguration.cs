using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class PrepaidWalletConfiguration : IEntityTypeConfiguration<PrepaidWallet>
{
    public void Configure(EntityTypeBuilder<PrepaidWallet> builder)
    {
        builder.ToTable("PrepaidWallets");
        builder.HasKey(w => w.Id);

        builder.Property(w => w.Balance)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.Property(w => w.EmergencyCreditLimit)
            .HasColumnType("decimal(18,2)")
            .IsRequired();

        builder.HasIndex(w => w.ConsumerId).IsUnique();

        // The ledger is exposed only as a read-only collection backed by a private List<T>
        // field; map the navigation to that field so EF can materialize/track it.
        builder.HasMany(w => w.Transactions)
            .WithOne()
            .HasForeignKey(t => t.WalletId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(w => w.Transactions)
            .UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
