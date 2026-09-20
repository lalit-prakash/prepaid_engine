using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

public class PasswordResetRequestConfiguration : IEntityTypeConfiguration<PasswordResetRequest>
{
    public void Configure(EntityTypeBuilder<PasswordResetRequest> b)
    {
        b.ToTable("PasswordResetRequests");
        b.HasKey(r => r.Id);
        b.Property(r => r.LoginId).IsRequired().HasMaxLength(100);
        b.Property(r => r.OtpHash).IsRequired().HasMaxLength(100);
        b.Property(r => r.Salt).IsRequired().HasMaxLength(100);
        b.Property(r => r.RequestedFromIp).HasMaxLength(64);
        b.HasIndex(r => new { r.LoginId, r.RequestedAt });
    }
}

public class UserPasswordOverrideConfiguration : IEntityTypeConfiguration<UserPasswordOverride>
{
    public void Configure(EntityTypeBuilder<UserPasswordOverride> b)
    {
        b.ToTable("UserPasswordOverrides");
        b.HasKey(o => o.LoginId);
        b.Property(o => o.LoginId).HasMaxLength(100).ValueGeneratedNever();
        b.Property(o => o.PasswordHash).IsRequired().HasMaxLength(200);
    }
}
