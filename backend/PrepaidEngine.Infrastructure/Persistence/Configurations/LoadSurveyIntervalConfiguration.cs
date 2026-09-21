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

        builder.Property(l => l.IntervalStart).IsRequired();
        builder.Property(l => l.IntervalEnd).IsRequired();
        builder.Property(l => l.ImportKwh).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(l => l.ImportKvah).HasColumnType("decimal(18,3)");
        builder.Property(l => l.ReceivedAt).IsRequired();
        builder.Property(l => l.SourceReference).HasMaxLength(100);

        builder.HasIndex(l => new { l.ConsumerId, l.MeterId, l.IntervalStart }).IsUnique();
        builder.HasIndex(l => l.MeterId);
        builder.HasIndex(l => l.IntervalStart);

        builder.HasOne<Consumer>().WithMany().HasForeignKey(l => l.ConsumerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SmartMeter>().WithMany().HasForeignKey(l => l.MeterId).OnDelete(DeleteBehavior.Restrict);
    }
}
