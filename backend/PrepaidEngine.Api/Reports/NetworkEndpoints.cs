using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Reports;

/// <summary>
/// GET /api/v1/network/nodes?level=zone|circle|division|subdivision|substation|feeder|dtr[&amp;parentId=...]
/// lists the nodes at one level (children of parentId, or all zones), ordered by name, so the report filters can
/// offer a cascading choice. Each level's parent is the level above it.
/// </summary>
public static class NetworkEndpoints
{
    private const int MaxNodes = 500;

    public static void MapNetworkEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/network/nodes", async (string level, Guid? parentId, PrepaidEngineDbContext db) =>
        {
            if (!Hierarchy.IsLevel(level)) return Results.BadRequest(new { error = "Unknown level." });

            var nodes = level.ToLowerInvariant() switch
            {
                "zone" => await db.Zones.AsNoTracking().OrderBy(n => n.Name).Take(MaxNodes).Select(n => new NodeDto(n.Id, n.Code, n.Name)).ToListAsync(),
                "circle" => await db.Circles.AsNoTracking().Where(n => parentId == null || n.ZoneId == parentId).OrderBy(n => n.Name).Take(MaxNodes).Select(n => new NodeDto(n.Id, n.Code, n.Name)).ToListAsync(),
                "division" => await db.Divisions.AsNoTracking().Where(n => parentId == null || n.CircleId == parentId).OrderBy(n => n.Name).Take(MaxNodes).Select(n => new NodeDto(n.Id, n.Code, n.Name)).ToListAsync(),
                "subdivision" => await db.SubDivisions.AsNoTracking().Where(n => parentId == null || n.DivisionId == parentId).OrderBy(n => n.Name).Take(MaxNodes).Select(n => new NodeDto(n.Id, n.Code, n.Name)).ToListAsync(),
                "substation" => await db.Substations.AsNoTracking().Where(n => parentId == null || n.SubDivisionId == parentId).OrderBy(n => n.Name).Take(MaxNodes).Select(n => new NodeDto(n.Id, n.Code, n.Name)).ToListAsync(),
                "feeder" => await db.Feeders.AsNoTracking().Where(n => parentId == null || n.SubstationId == parentId).OrderBy(n => n.Name).Take(MaxNodes).Select(n => new NodeDto(n.Id, n.Code, n.Name)).ToListAsync(),
                _ => await db.Dtrs.AsNoTracking().Where(n => parentId == null || n.FeederId == parentId).OrderBy(n => n.Name).Take(MaxNodes).Select(n => new NodeDto(n.Id, n.Code, n.Name)).ToListAsync(),
            };
            return Results.Ok(nodes);
        })
        .WithName("ListNetworkNodes")
        .RequireAuthorization();
    }

    private sealed record NodeDto(Guid Id, string Code, string Name);
}
