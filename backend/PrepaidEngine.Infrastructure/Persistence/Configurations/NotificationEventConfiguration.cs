using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class NotificationEventConfiguration : IEntityTypeConfiguration<NotificationEvent>
{
    public void Configure(EntityTypeBuilder<NotificationEvent> builder)
    {
        builder.ToTable("NotificationEvents");
        builder.HasKey(n => n.Id);

        builder.Property(n => n.EventType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(n => n.Message).IsRequired().HasMaxLength(1000);
        builder.Property(n => n.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(n => n.ProviderReference).HasMaxLength(100);
        builder.Property(n => n.CreatedAt).IsRequired();

        builder.HasIndex(n => n.ConsumerId);

        builder.HasOne<Consumer>().WithMany().HasForeignKey(n => n.ConsumerId).OnDelete(DeleteBehavior.Restrict);
    }
}
