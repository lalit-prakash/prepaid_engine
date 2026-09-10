using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class ConnectivityCommandConfiguration : IEntityTypeConfiguration<ConnectivityCommand>
{
    public void Configure(EntityTypeBuilder<ConnectivityCommand> builder)
    {
        builder.ToTable("ConnectivityCommands");
        builder.HasKey(c => c.Id);

        builder.Property(c => c.CommandType)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(c => c.Reason).IsRequired().HasMaxLength(500);

        builder.Property(c => c.Status)
            .HasConversion<string>()
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(c => c.RetryCount).IsRequired();
        builder.Property(c => c.ErrorMessage).HasMaxLength(500);

        builder.Property(c => c.CreatedAt).IsRequired();

        // Unlike MeterCommand (at most one per recharge), a consumer can be disconnected and
        // later reconnected any number of times over its lifetime, so ConsumerId is indexed for
        // lookup but never unique.
        builder.HasIndex(c => c.ConsumerId);

        builder.HasOne<Consumer>()
            .WithMany()
            .HasForeignKey(c => c.ConsumerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
