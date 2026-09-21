using Microsoft.AspNetCore.Authorization;
using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Api.Auth;

/// <summary>What a role is for, in plain words (shown on the Roles &amp; Permissions tab).</summary>
public sealed record RoleInfo(UserRole Role, string Summary);

/// <summary>One thing a person may do, the authorization policy that enforces it, and the roles it is granted to.</summary>
public sealed record PermissionInfo(string Policy, string Name, string Description, UserRole[] Roles);

/// <summary>
/// The single definition of who may do what. The authorization policies the endpoints require are registered from this list, and the
/// Roles &amp; Permissions screen reads the same list, so what the screen shows is exactly what the API enforces. Read endpoints only need a
/// signed-in user (any role); every write endpoint names one of the other policies (checked at startup).
/// </summary>
public static class AccessPolicies
{
    public const string Authenticated = "Authenticated";
    public const string Operations = "Operations";
    public const string DataAdmin = "DataAdmin";
    public const string ITRole = "ITRole";
    public const string UtilityRole = "UtilityRole";
    public const string TariffGovernanceRole = "TariffGovernanceRole";

    private static readonly UserRole[] Everyone = Enum.GetValues<UserRole>();

    public static readonly RoleInfo[] Roles =
    {
        new(UserRole.Admin, "Full access, including user management and system settings."),
        new(UserRole.IT, "Runs the system: operations, bulk data, tariff drafting, user management and settings."),
        new(UserRole.Operator, "Day-to-day operations: recharges, disconnect / reconnect, retries, reconciliation and exceptions."),
        new(UserRole.Utility, "Utility management: approves tariff changes. Cannot operate or change data."),
        new(UserRole.ReadOnly, "Can view everything; cannot change anything."),
    };

    public static readonly PermissionInfo[] Permissions =
    {
        new(Authenticated, "View data", "See every list, report and dashboard.", Everyone),
        new(Operations, "Operate", "Recharge, disconnect / reconnect, retry commands, conversions, reconciliation, exceptions, meter replacements, billing holds and alarms.", new[] { UserRole.Admin, UserRole.IT, UserRole.Operator }),
        new(DataAdmin, "Bulk data and billing runs", "Load the network hierarchy, ingest meter data and run billing.", new[] { UserRole.Admin, UserRole.IT }),
        new(ITRole, "Users, settings and tariff drafting", "Manage users, change system settings and draft tariff changes.", new[] { UserRole.Admin, UserRole.IT }),
        new(UtilityRole, "Approve tariff changes", "Approve or reject a drafted tariff change (so no one approves their own).", new[] { UserRole.Utility }),
        new(TariffGovernanceRole, "Tariff governance", "See tariff change requests and their history.", new[] { UserRole.Admin, UserRole.IT, UserRole.Utility }),
    };

    /// <summary>Registers one authorization policy per permission.</summary>
    public static void Register(AuthorizationOptions options)
    {
        foreach (var p in Permissions)
        {
            if (p.Policy == Authenticated) options.AddPolicy(p.Policy, policy => policy.RequireAuthenticatedUser());
            else options.AddPolicy(p.Policy, policy => policy.RequireRole(p.Roles.Select(r => r.ToString()).ToArray()));
        }
    }
}
