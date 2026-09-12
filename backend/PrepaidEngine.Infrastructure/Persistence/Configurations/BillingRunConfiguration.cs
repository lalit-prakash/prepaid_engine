using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class BillingRunConfiguration : IEntityTypeConfiguration<BillingRun>
{
    public void Configure(EntityTypeBuilder<BillingRun> builder)
    {
        builder.ToTable("BillingRuns");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.RunType).IsRequired().HasMaxLength(30);
        builder.Property(r => r.BillingDate).IsRequired();
        builder.Property(r => r.StartedAt).IsRequired();
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(30).IsRequired();

        // Prevents two daily billing runs from being created for the same date (spec §22).
        builder.HasIndex(r => new { r.RunType, r.BillingDate }).IsUnique();
    }
}
