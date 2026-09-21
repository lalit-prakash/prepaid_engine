using System.Globalization;
using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Auth;
using PrepaidEngine.Api.Auth.Email;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Settings;

public sealed record SaveSettingsRequest(Dictionary<string, string?>? Values);

/// <summary>
/// System Settings. GET shows every editable setting with its current value, its default and who last changed it, plus a few read-only facts
/// about how the system is set up. PUT saves changes (a blank value puts a setting back to its default): all-or-nothing, validated, audited,
/// and applied without a restart. Admin and IT only.
/// </summary>
public static class SettingsEndpoints
{
    public static void MapSettingsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/settings", async (PrepaidEngineDbContext db, IConfiguration config, SettingsReloader settings, IEmailSender email, IWebHostEnvironment env, Microsoft.Extensions.Options.IOptions<JwtOptions> jwt) =>
        {
            var saved = await db.SystemSettings.AsNoTracking().ToDictionaryAsync(s => s.Key);
            var groups = SettingsRegistry.All.GroupBy(d => d.Group).Select(g => new
            {
                Name = g.Key,
                Settings = g.Select(d =>
                {
                    saved.TryGetValue(d.Key, out var row);
                    var defaultValue = settings.DefaultValue(d.Key);
                    return new
                    {
                        d.Key, d.Label, d.Description, d.Unit,
                        Type = d.Type.ToString(), d.Min, d.Max, d.AllowBlank,
                        Value = row?.Value ?? defaultValue,
                        DefaultValue = defaultValue,
                        IsCustom = row is not null,
                        UpdatedAt = row?.UpdatedAt,
                        UpdatedBy = row?.UpdatedBy,
                    };
                }),
            });

            var readOnly = new[]
            {
                new { Label = "Environment", Value = env.EnvironmentName },
                new { Label = "Sign-in session", Value = $"{jwt.Value.AccessTokenMinutes} minutes, renewable for up to {jwt.Value.MaxSessionHours} hours" },
                new { Label = "Sign-in lock", Value = $"{LoginThrottle.MaxFailures} failed attempts lock a login id for {(int)LoginThrottle.LockDuration.TotalMinutes} minutes" },
                new { Label = "E-mail for password reset", Value = email.CanSend ? (string.IsNullOrWhiteSpace(config["Email:Host"]) ? "Development only: codes are written to the API console" : "Sending through " + config["Email:Host"]) : "Not set up" },
                new { Label = "Report exports kept for", Value = $"{config["ReportJobs:RetentionDays"] ?? "7"} days" },
            };
            return Results.Ok(new { Groups = groups, ReadOnly = readOnly, Note = "Session, lock and e-mail settings hold secrets or need a restart, so they are changed in the API's configuration." });
        })
        .WithName("GetSettings")
        .RequireAuthorization(AccessPolicies.ITRole);

        app.MapPut("/api/v1/settings", async (SaveSettingsRequest request, ClaimsPrincipal principal, PrepaidEngineDbContext db, IConfiguration config, SettingsReloader reloader) =>
        {
            if (request.Values is null || request.Values.Count == 0) return Results.BadRequest(new { error = "Nothing to save." });

            var problems = new List<string>();
            foreach (var (key, raw) in request.Values)
            {
                var d = SettingsRegistry.Find(key);
                if (d is null) { problems.Add($"'{key}' is not a setting that can be changed."); continue; }
                if (SettingsRegistry.Validate(d, raw) is { } problem) problems.Add(problem);
            }
            if (problems.Count > 0) return Results.BadRequest(new { error = problems[0], problems });

            // Cross-check with the values that will be in force after this save.
            string? Effective(string key) => request.Values.TryGetValue(key, out var v) ? (string.IsNullOrWhiteSpace(v) ? reloader.DefaultValue(key) : v) : config[key];
            if (decimal.TryParse(Effective("EnergyValidation:WarningTolerancePct"), NumberStyles.Number, CultureInfo.InvariantCulture, out var warn)
                && decimal.TryParse(Effective("EnergyValidation:FailTolerancePct"), NumberStyles.Number, CultureInfo.InvariantCulture, out var fail) && fail < warn)
                return Results.BadRequest(new { error = "The failure tolerance must not be lower than the warning tolerance.", problems = new[] { "The failure tolerance must not be lower than the warning tolerance." } });

            var actor = principal.Identity?.Name ?? "unknown";
            var now = DateTime.UtcNow;
            var existing = await db.SystemSettings.ToDictionaryAsync(s => s.Key);
            var changed = 0;
            foreach (var (key, raw) in request.Values)
            {
                existing.TryGetValue(key, out var row);
                var before = row?.Value ?? reloader.DefaultValue(key) ?? "(not set)";
                if (string.IsNullOrWhiteSpace(raw))
                {
                    if (row is null) continue;
                    db.SystemSettings.Remove(row);
                    db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), "SystemSetting", key, "SETTING_RESET", actor, now, before, reloader.DefaultValue(key) ?? "(not set)"));
                    changed++;
                    continue;
                }

                var value = SettingsRegistry.Normalise(raw);
                if (row is null) db.SystemSettings.Add(new SystemSetting(key, value, now, actor));
                else if (row.Value != value) row.Change(value, now, actor);
                else continue;
                db.AuditEntries.Add(new AuditEntry(Guid.NewGuid(), "SystemSetting", key, "SETTING_CHANGED", actor, now, before, value));
                changed++;
            }

            if (changed > 0)
            {
                await db.SaveChangesAsync();
                reloader.Reload(); // the new values apply from the next request, no restart
            }
            return Results.Ok(new { changed, message = changed == 0 ? "No changes." : $"Saved {changed} setting(s). They apply now." });
        })
        .WithName("SaveSettings")
        .RequireAuthorization(AccessPolicies.ITRole);
    }
}
