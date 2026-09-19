using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class ReportJobConfiguration : IEntityTypeConfiguration<ReportJob>
{
    public void Configure(EntityTypeBuilder<ReportJob> builder)
    {
        builder.ToTable("ReportJobs");
        builder.HasKey(j => j.Id);
        builder.Property(j => j.ReportKey).IsRequired().HasMaxLength(60);
        builder.Property(j => j.ParametersJson).IsRequired().HasMaxLength(2000);
        builder.Property(j => j.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(j => j.RequestedBy).IsRequired().HasMaxLength(200);
        builder.Property(j => j.FileName).HasMaxLength(100);
        builder.Property(j => j.Error).HasMaxLength(500);

        builder.HasIndex(j => new { j.RequestedBy, j.RequestedAt });
        // The worker only ever looks for the few jobs still waiting or running.
        builder.HasIndex(j => new { j.Status, j.RequestedAt }).HasFilter("\"Status\" IN ('Queued', 'Running')");
    }
}
