using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> b)
    {
        b.ToTable("Users");
        b.HasKey(u => u.Id);
        b.Property(u => u.LoginId).IsRequired().HasMaxLength(100);
        b.Property(u => u.LoginKey).IsRequired().HasMaxLength(100);
        b.HasIndex(u => u.LoginKey).IsUnique();
        b.Property(u => u.DisplayName).IsRequired().HasMaxLength(150);
        b.Property(u => u.Email).IsRequired().HasMaxLength(200);
        b.Property(u => u.PasswordHash).IsRequired().HasMaxLength(200);
        b.Property(u => u.CreatedBy).IsRequired().HasMaxLength(100);
        b.Property(u => u.Role).HasConversion<string>().HasMaxLength(20);
    }
}
