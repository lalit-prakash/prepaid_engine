using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class DailyLoadProfileConfiguration : IEntityTypeConfiguration<DailyLoadProfile>
{
    public void Configure(EntityTypeBuilder<DailyLoadProfile> builder)
    {
        builder.ToTable("DailyLoadProfiles");
        builder.HasKey(d => d.Id);

        builder.Property(d => d.ProfileDate).IsRequired();
        builder.Property(d => d.GeneratedAt).IsRequired();
        builder.Property(d => d.ReceivedAt).IsRequired();
        builder.Property(d => d.StartCumulativeKwh).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(d => d.EndCumulativeKwh).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(d => d.TotalKwh).HasColumnType("decimal(18,3)").IsRequired();
        builder.Property(d => d.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(d => d.SourceReference).HasMaxLength(100);

        // One DLP per Consumer+Meter+ProfileDate (spec §11.3).
        builder.HasIndex(d => new { d.ConsumerId, d.MeterId, d.ProfileDate }).IsUnique();

        builder.HasOne<Consumer>().WithMany().HasForeignKey(d => d.ConsumerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SmartMeter>().WithMany().HasForeignKey(d => d.MeterId).OnDelete(DeleteBehavior.Restrict);
    }
}
