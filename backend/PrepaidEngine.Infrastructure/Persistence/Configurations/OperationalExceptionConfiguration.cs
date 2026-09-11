using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class OperationalExceptionConfiguration : IEntityTypeConfiguration<OperationalException>
{
    public void Configure(EntityTypeBuilder<OperationalException> builder)
    {
        builder.ToTable("OperationalExceptions");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.SourceType).HasConversion<string>().HasMaxLength(30).IsRequired();
        builder.Property(e => e.Description).IsRequired().HasMaxLength(1000);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20).IsRequired();
        builder.Property(e => e.ResolutionNote).HasMaxLength(1000);
        builder.Property(e => e.CreatedAt).IsRequired();

        builder.HasIndex(e => new { e.SourceType, e.SourceId });
        builder.HasIndex(e => e.ConsumerId);
        builder.HasIndex(e => e.Status);

        builder.HasOne<Consumer>()
            .WithMany()
            .HasForeignKey(e => e.ConsumerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
