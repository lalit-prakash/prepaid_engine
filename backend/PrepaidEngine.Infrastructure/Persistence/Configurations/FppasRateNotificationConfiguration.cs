using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class FppasRateNotificationConfiguration : IEntityTypeConfiguration<FppasRateNotification>
{
    public void Configure(EntityTypeBuilder<FppasRateNotification> builder)
    {
        builder.ToTable("FppasRateNotifications");
        builder.HasKey(f => f.Id);

        builder.Property(f => f.RateFraction).HasColumnType("decimal(9,4)").IsRequired();
        builder.Property(f => f.NotifiedAt).IsRequired();
        builder.Property(f => f.ApplicableBillingMonth).IsRequired();

        builder.HasIndex(f => f.ApplicableBillingMonth).IsUnique();
    }
}
