using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class LoadSurveyIntervalConfiguration : IEntityTypeConfiguration<LoadSurveyInterval>
{
    public void Configure(EntityTypeBuilder<LoadSurveyInterval> builder)
    {
        builder.ToTable("LoadSurveyIntervals");
        builder.HasKey(l => l.Id);

        builder.Property(l => l.CumulativeKwh).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(l => l.IntervalKwh).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(l => l.Quality).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(l => l.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(l => l.SourceReference).HasMaxLength(100);
        builder.Property(l => l.IntervalStart).IsRequired();
        builder.Property(l => l.IntervalEnd).IsRequired();
        builder.Property(l => l.ReceivedAt).IsRequired();

        // Duplicate-block protection (spec §5.2) — the final safety barrier under concurrent ingestion.
        builder.HasIndex(l => new { l.MeterId, l.IntervalStart, l.IntervalEnd }).IsUnique();
        builder.HasIndex(l => l.ConsumerId);

        builder.HasOne<Consumer>().WithMany().HasForeignKey(l => l.ConsumerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SmartMeter>().WithMany().HasForeignKey(l => l.MeterId).OnDelete(DeleteBehavior.Restrict);
    }
}
