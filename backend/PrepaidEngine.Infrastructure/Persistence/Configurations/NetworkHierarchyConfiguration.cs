using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence.Configurations;

// One small configuration per level: a table, a unique code, a required name and an indexed link to the parent.
internal static class NetworkNodeMapping
{
    public static void Base<T>(EntityTypeBuilder<T> b, string table) where T : NetworkNodeBase
    {
        b.ToTable(table);
        b.HasKey(x => x.Id);
        b.Property(x => x.Code).IsRequired().HasMaxLength(40);
        b.Property(x => x.Name).IsRequired().HasMaxLength(200);
        b.HasIndex(x => x.Code).IsUnique();
    }
}

public class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> b) => NetworkNodeMapping.Base(b, "Zones");
}

public class CircleConfiguration : IEntityTypeConfiguration<Circle>
{
    public void Configure(EntityTypeBuilder<Circle> b)
    {
        NetworkNodeMapping.Base(b, "Circles");
        b.HasOne(x => x.Zone).WithMany().HasForeignKey(x => x.ZoneId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.ZoneId);
    }
}

public class DivisionConfiguration : IEntityTypeConfiguration<Division>
{
    public void Configure(EntityTypeBuilder<Division> b)
    {
        NetworkNodeMapping.Base(b, "Divisions");
        b.HasOne(x => x.Circle).WithMany().HasForeignKey(x => x.CircleId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.CircleId);
    }
}

public class SubDivisionConfiguration : IEntityTypeConfiguration<SubDivision>
{
    public void Configure(EntityTypeBuilder<SubDivision> b)
    {
        NetworkNodeMapping.Base(b, "SubDivisions");
        b.HasOne(x => x.Division).WithMany().HasForeignKey(x => x.DivisionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.DivisionId);
    }
}

public class SubstationConfiguration : IEntityTypeConfiguration<Substation>
{
    public void Configure(EntityTypeBuilder<Substation> b)
    {
        NetworkNodeMapping.Base(b, "Substations");
        b.HasOne(x => x.SubDivision).WithMany().HasForeignKey(x => x.SubDivisionId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.SubDivisionId);
    }
}

public class FeederConfiguration : IEntityTypeConfiguration<Feeder>
{
    public void Configure(EntityTypeBuilder<Feeder> b)
    {
        NetworkNodeMapping.Base(b, "Feeders");
        b.HasOne(x => x.Substation).WithMany().HasForeignKey(x => x.SubstationId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.SubstationId);
    }
}

public class DtrConfiguration : IEntityTypeConfiguration<Dtr>
{
    public void Configure(EntityTypeBuilder<Dtr> b)
    {
        NetworkNodeMapping.Base(b, "Dtrs");
        b.HasOne(x => x.Feeder).WithMany().HasForeignKey(x => x.FeederId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.FeederId);
    }
}
