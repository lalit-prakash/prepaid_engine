using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class SystemSettingConfiguration : IEntityTypeConfiguration<SystemSetting>
{
    public void Configure(EntityTypeBuilder<SystemSetting> b)
    {
        b.ToTable("SystemSettings");
        b.HasKey(s => s.Key);
        b.Property(s => s.Key).HasMaxLength(100).ValueGeneratedNever();
        b.Property(s => s.Value).IsRequired().HasMaxLength(500);
        b.Property(s => s.UpdatedBy).IsRequired().HasMaxLength(100);
    }
}
