using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class MeterAlarmConfiguration : IEntityTypeConfiguration<MeterAlarm>
{
    public void Configure(EntityTypeBuilder<MeterAlarm> builder)
    {
        builder.ToTable("MeterAlarms");
        builder.HasKey(a => a.Id);

        builder.Property(a => a.AlarmCode).HasConversion<string>().HasMaxLength(40).IsRequired();
        builder.Property(a => a.Severity).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.RaisedAt).IsRequired();
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(a => a.AcknowledgedBy).HasMaxLength(100);
        builder.Property(a => a.ResolutionNote).HasMaxLength(1000);
        builder.Property(a => a.ReceivedAt).IsRequired();
        builder.Property(a => a.SourceReference).HasMaxLength(100);

        builder.HasIndex(a => new { a.MeterId, a.AlarmCode, a.RaisedAt }).IsUnique();
        builder.HasIndex(a => a.ConsumerId);
        builder.HasIndex(a => a.Status);

        builder.HasOne<Consumer>().WithMany().HasForeignKey(a => a.ConsumerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<SmartMeter>().WithMany().HasForeignKey(a => a.MeterId).OnDelete(DeleteBehavior.Restrict);
    }
}
