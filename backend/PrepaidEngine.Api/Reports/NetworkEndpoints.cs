using System.Security.Claims;
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

        // How much of the network is loaded and how many consumers are still unmapped: counts only, in SQL.
        app.MapGet("/api/v1/network/summary", async (PrepaidEngineDbContext db) => Results.Ok(new
        {
            Zones = await db.Zones.CountAsync(),
            Circles = await db.Circles.CountAsync(),
            Divisions = await db.Divisions.CountAsync(),
            SubDivisions = await db.SubDivisions.CountAsync(),
            Substations = await db.Substations.CountAsync(),
            Feeders = await db.Feeders.CountAsync(),
            Dtrs = await db.Dtrs.CountAsync(),
            ConsumersMapped = await db.Consumers.CountAsync(c => c.DtrId != null),
            ConsumersUnmapped = await db.Consumers.CountAsync(c => c.DtrId == null),
        }))
        .WithName("NetworkSummary")
        .RequireAuthorization();

        // Import the hierarchy from flat rows (one path down to a DTR per row). dryRun validates and reports without saving.
        app.MapPost("/api/v1/network/import", async (NetworkImportRequest request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
        {
            if (request.Rows is null || request.Rows.Count == 0) return Results.BadRequest(new { error = "The file has no rows." });
            if (request.Rows.Count > NetworkImport.MaxRows) return Results.BadRequest(new { error = $"At most {NetworkImport.MaxRows} rows per request." });
            return Results.Ok(await NetworkImport.ImportHierarchyAsync(db, request.Rows, request.DryRun, user.Identity?.Name ?? "unknown"));
        })
        .WithName("ImportNetworkHierarchy")
        .RequireAuthorization("DataAdmin");

        // Map consumers to DTRs by account number and DTR code.
        app.MapPost("/api/v1/network/consumer-mapping", async (ConsumerMappingRequest request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
        {
            if (request.Rows is null || request.Rows.Count == 0) return Results.BadRequest(new { error = "The file has no rows." });
            if (request.Rows.Count > NetworkImport.MaxRows) return Results.BadRequest(new { error = $"At most {NetworkImport.MaxRows} rows per request." });
            return Results.Ok(await NetworkImport.MapConsumersAsync(db, request.Rows, request.DryRun, user.Identity?.Name ?? "unknown"));
        })
        .WithName("MapConsumersToDtr")
        .RequireAuthorization("DataAdmin");
    }

    private sealed record NodeDto(Guid Id, string Code, string Name);

    public sealed record NetworkImportRequest(List<NetworkImportRow>? Rows, bool DryRun);

    public sealed record ConsumerMappingRequest(List<ConsumerMappingRow>? Rows, bool DryRun);
}
