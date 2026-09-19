using System.Linq.Expressions;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Api.Reports;

/// <summary>Optional network-hierarchy filters, bound from the query string on every report. Filtering by a
/// level keeps only consumers supplied from a DTR under that node.</summary>
public sealed record HierarchyQuery(Guid? ZoneId, Guid? CircleId, Guid? DivisionId, Guid? SubDivisionId, Guid? SubstationId, Guid? FeederId, Guid? DtrId);

/// <summary>A consumer id with the hierarchy name to group by (null when no grouping level was asked for).</summary>
public sealed class ConsumerKey
{
    public Guid Id { get; set; }
    public string? Key { get; set; }
}

public static class Hierarchy
{
    public static readonly string[] Levels = { "zone", "circle", "division", "subdivision", "substation", "feeder", "dtr" };

    /// <summary>Keeps only consumers under the selected nodes. A no-op when no filter is given, so it adds no join.</summary>
    public static IQueryable<Consumer> ByHierarchy(this IQueryable<Consumer> q, HierarchyQuery h)
    {
        if (h.DtrId is { } dtr) q = q.Where(c => c.DtrId == dtr);
        if (h.FeederId is { } feeder) q = q.Where(c => c.Dtr!.FeederId == feeder);
        if (h.SubstationId is { } substation) q = q.Where(c => c.Dtr!.Feeder.SubstationId == substation);
        if (h.SubDivisionId is { } subDivision) q = q.Where(c => c.Dtr!.Feeder.Substation.SubDivisionId == subDivision);
        if (h.DivisionId is { } division) q = q.Where(c => c.Dtr!.Feeder.Substation.SubDivision.DivisionId == division);
        if (h.CircleId is { } circle) q = q.Where(c => c.Dtr!.Feeder.Substation.SubDivision.Division.CircleId == circle);
        if (h.ZoneId is { } zone) q = q.Where(c => c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.ZoneId == zone);
        return q;
    }

    /// <summary>Whether a grouping level name is one of the seven known levels.</summary>
    public static bool IsLevel(string? level) => level is not null && Levels.Contains(level.ToLowerInvariant());

    /// <summary>Projects each consumer to its id and the name of its node at the chosen level.</summary>
    public static Expression<Func<Consumer, ConsumerKey>> KeyFor(string? level) => level?.ToLowerInvariant() switch
    {
        "zone" => c => new ConsumerKey { Id = c.Id, Key = c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Zone.Name },
        "circle" => c => new ConsumerKey { Id = c.Id, Key = c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Name },
        "division" => c => new ConsumerKey { Id = c.Id, Key = c.Dtr!.Feeder.Substation.SubDivision.Division.Name },
        "subdivision" => c => new ConsumerKey { Id = c.Id, Key = c.Dtr!.Feeder.Substation.SubDivision.Name },
        "substation" => c => new ConsumerKey { Id = c.Id, Key = c.Dtr!.Feeder.Substation.Name },
        "feeder" => c => new ConsumerKey { Id = c.Id, Key = c.Dtr!.Feeder.Name },
        "dtr" => c => new ConsumerKey { Id = c.Id, Key = c.Dtr!.Name },
        _ => c => new ConsumerKey { Id = c.Id, Key = null },
    };
}
