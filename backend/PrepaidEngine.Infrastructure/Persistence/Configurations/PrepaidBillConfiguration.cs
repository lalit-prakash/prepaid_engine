using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class PrepaidBillConfiguration : IEntityTypeConfiguration<PrepaidBill>
{
    public void Configure(EntityTypeBuilder<PrepaidBill> builder)
    {
        builder.ToTable("PrepaidBills");
        builder.HasKey(b => b.Id);

        builder.Property(b => b.Amount).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.AmountPaid).HasColumnType("decimal(18,2)").IsRequired();
        builder.Property(b => b.GeneratedAt).IsRequired();

        builder.Property(b => b.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.HasIndex(b => b.ConsumerId);
        builder.HasIndex(b => b.ConsumptionReadingId).IsUnique();

        builder.HasOne<Consumer>()
            .WithMany()
            .HasForeignKey(b => b.ConsumerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<ConsumptionReading>()
            .WithMany()
            .HasForeignKey(b => b.ConsumptionReadingId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<Tariff>()
            .WithMany()
            .HasForeignKey(b => b.TariffId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
