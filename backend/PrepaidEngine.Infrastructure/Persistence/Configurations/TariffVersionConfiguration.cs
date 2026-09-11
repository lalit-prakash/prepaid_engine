using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class TariffVersionConfiguration : IEntityTypeConfiguration<TariffVersion>
{
    public void Configure(EntityTypeBuilder<TariffVersion> builder)
    {
        builder.ToTable("TariffVersions");
        builder.HasKey(v => v.Id);

        builder.Property(v => v.FieldName).IsRequired().HasMaxLength(100);
        builder.Property(v => v.OldValue).IsRequired().HasMaxLength(200);
        builder.Property(v => v.NewValue).IsRequired().HasMaxLength(200);
        builder.Property(v => v.ChangeNote).IsRequired().HasMaxLength(1000);
        builder.Property(v => v.EffectiveDate).IsRequired();
        builder.Property(v => v.RecordedAt).IsRequired();

        builder.HasIndex(v => v.TariffId);

        builder.HasOne<Tariff>()
            .WithMany()
            .HasForeignKey(v => v.TariffId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
