using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class ConversionRequestConfiguration : IEntityTypeConfiguration<ConversionRequest>
{
    public void Configure(EntityTypeBuilder<ConversionRequest> builder)
    {
        builder.ToTable("ConversionRequests");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.TransactionId).IsRequired().HasMaxLength(100);
        builder.Property(c => c.MeterSerialNumber).IsRequired().HasMaxLength(50);
        builder.Property(c => c.ConsumerNumber).IsRequired().HasMaxLength(30);
        builder.Property(c => c.RequestType).IsRequired().HasMaxLength(10);
        builder.Property(c => c.ConsumerType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(c => c.InitialReading).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(c => c.InitialReadingDateTime).IsRequired();
        builder.Property(c => c.ConversionDate).IsRequired();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(c => c.DecisionNote).HasMaxLength(500);
        builder.Property(c => c.RequestedAt).IsRequired();

        builder.HasIndex(c => c.ConsumerId);
        builder.HasIndex(c => c.TransactionId).IsUnique();

        builder.HasOne<Consumer>()
            .WithMany()
            .HasForeignKey(c => c.ConsumerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
