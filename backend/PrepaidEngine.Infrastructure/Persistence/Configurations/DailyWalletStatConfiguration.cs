using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class DailyWalletStatConfiguration : IEntityTypeConfiguration<DailyWalletStat>
{
    public void Configure(EntityTypeBuilder<DailyWalletStat> builder)
    {
        builder.ToTable("DailyWalletStats");
        builder.HasKey(s => s.Date);
        builder.Property(s => s.Date).ValueGeneratedNever();
        builder.Property(s => s.WalletTotal).HasColumnType("decimal(18,2)");
        builder.Property(s => s.RecordedAt).IsRequired();
    }
}
