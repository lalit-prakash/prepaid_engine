using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Auth;
using PrepaidEngine.Api.Security;
using PrepaidEngine.Api.Dashboard;
using PrepaidEngine.Api.Reports;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Application.Conversion;
using PrepaidEngine.Application.MeterCommands;
using PrepaidEngine.Application.MeterData;
using PrepaidEngine.Application.Rms;
using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Billing;
using PrepaidEngine.Infrastructure.Connectivity;
using PrepaidEngine.Infrastructure.Conversion;
using PrepaidEngine.Infrastructure.MeterCommands;
using PrepaidEngine.Infrastructure.MeterData;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Sla;
using PrepaidEngine.Infrastructure.Tariffs;
using PrepaidEngine.Application.Sla;
using PrepaidEngine.Infrastructure.Persistence.Seed;
using PrepaidEngine.Infrastructure.Rms;
using static PrepaidEngine.Api.ApiHelpers;

namespace PrepaidEngine.Api.Endpoints;

/// <summary>Platform endpoints, moved out of Program.cs unchanged.</summary>
public static class PlatformEndpoints
{
    public static void MapPlatformEndpoints(this WebApplication app)
    {
        app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
            .WithName("Health");


        // Tells the caller who they are and which of the two tariff-governance roles (IT/Utility) their
        // credential carries, purely so the frontend can decide which actions to render — the backend's
        // own RequireAuthorization("ITRole"/"UtilityRole") policies are the actual security boundary and
        // are enforced independently of what this endpoint returns.
        app.MapGet("/api/v1/auth/whoami", (ClaimsPrincipal user) =>
            Results.Ok(new
            {
                Username = user.Identity?.Name ?? "unknown",
                DisplayName = user.FindFirst(TokenService.DisplayNameClaim)?.Value ?? user.Identity?.Name ?? "unknown",
                Role = user.FindFirst(ClaimTypes.Role)?.Value,
            }))
        .WithName("WhoAmI")
        .RequireAuthorization();
    }
}
