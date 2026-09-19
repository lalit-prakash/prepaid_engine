using System.Reflection;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authorization.Infrastructure;
using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Routing;

namespace PrepaidEngine.Api.Docs;

/// <summary>
/// Writes docs/API_REFERENCE.md from the endpoints the running app actually registers: method, path, who may call it
/// (read from the authorization metadata and the policies themselves), and every path, query and body parameter.
/// Regenerate it after adding or changing an endpoint:
///   ASPNETCORE_ENVIRONMENT=Production Jwt__Key=&lt;any 32+ characters&gt; dotnet run --project backend/PrepaidEngine.Api -- dump-endpoints docs/API_REFERENCE.md
/// (Production mode is used only so the app builds without touching the database.)
/// </summary>
public static class ApiReference
{
    public static async Task WriteAsync(WebApplication app, string path)
    {
        var provider = app.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>();
        var policies = app.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var apis = provider.ApiDescriptionGroups.Items.SelectMany(g => g.Items).ToList();
        var policyNames = apis.SelectMany(a => a.ActionDescriptor.EndpointMetadata.OfType<IAuthorizeData>())
            .Select(d => d.Policy).Where(p => !string.IsNullOrEmpty(p)).Distinct().OrderBy(p => p).ToList();

        var roleByPolicy = new Dictionary<string, string>();
        foreach (var name in policyNames)
        {
            var policy = await policies.GetPolicyAsync(name!);
            var roles = policy?.Requirements.OfType<RolesAuthorizationRequirement>().SelectMany(r => r.AllowedRoles).ToList();
            roleByPolicy[name!] = roles is { Count: > 0 } ? string.Join(", ", roles) : "any signed-in user";
        }

        string Auth(ApiDescription a)
        {
            var meta = a.ActionDescriptor.EndpointMetadata;
            if (meta.OfType<IAllowAnonymous>().Any()) return "Anonymous";
            var policy = meta.OfType<IAuthorizeData>().Select(d => d.Policy).FirstOrDefault(p => !string.IsNullOrEmpty(p));
            if (policy is not null) return $"`{policy}` ({roleByPolicy[policy]})";
            return meta.OfType<IAuthorizeData>().Any() ? "Any signed-in user" : "Anonymous";
        }

        var rows = apis
            .Select(a => new
            {
                Method = a.HttpMethod ?? "?",
                Path = "/" + a.RelativePath!.TrimStart('/'),
                Name = a.ActionDescriptor.EndpointMetadata.OfType<IEndpointNameMetadata>().FirstOrDefault()?.EndpointName ?? "",
                Auth = Auth(a),
                Api = a,
            })
            .OrderBy(r => Section(r.Path)).ThenBy(r => r.Path).ThenBy(r => r.Method)
            .ToList();

        var sb = new StringBuilder();
        sb.AppendLine("# API reference");
        sb.AppendLine();
        sb.AppendLine("Every HTTP endpoint the Prepaid Engine API exposes: method, path, who may call it, and its parameters.");
        sb.AppendLine($"Generated from the running app ({rows.Count} endpoints), so it matches the code. Do not edit by hand; regenerate with:");
        sb.AppendLine();
        sb.AppendLine("```");
        sb.AppendLine("ASPNETCORE_ENVIRONMENT=Production Jwt__Key=<any 32+ characters> dotnet run --project backend/PrepaidEngine.Api -- dump-endpoints docs/API_REFERENCE.md");
        sb.AppendLine("```");
        sb.AppendLine();
        sb.AppendLine("## Conventions");
        sb.AppendLine();
        sb.AppendLine("- Base path `/api/v1` (except `/health`). JSON in and out. Send `Authorization: Bearer <token>` from `POST /api/v1/auth/login`.");
        sb.AppendLine("- Optionally send `X-Correlation-Id` (8 to 64 letters, digits or hyphens); it is echoed back and stored on audit entries.");
        sb.AppendLine("- Enums are sent as numbers unless a parameter is described as a string. Dates are ISO 8601 (`yyyy-MM-dd` or full UTC timestamps).");
        sb.AppendLine("- Paged searches return `{ items, nextCursor, totalCount }`; pass `nextCursor` back as `after`. `pageSize` is capped at 100.");
        sb.AppendLine("- Unpaged lists return at most 1,000 rows and set `X-Result-Truncated: true` when more exist. Reports return at most 5,000 rows and say so in `truncated`.");
        sb.AppendLine("- Errors: `400` validation, `401` missing or invalid token, `403` your role may not do this, `404` not found, `409` conflict, `429` rate limit (`Retry-After` is set), `503` an upstream adapter is down.");
        sb.AppendLine();
        sb.AppendLine("## Access policies");
        sb.AppendLine();
        sb.AppendLine("| Policy | Who may call |");
        sb.AppendLine("|---|---|");
        sb.AppendLine("| Any signed-in user | every role, including `ReadOnly` (all reads use this) |");
        foreach (var name in policyNames) sb.AppendLine($"| `{name}` | {roleByPolicy[name!]} |");
        sb.AppendLine();
        sb.AppendLine("Roles: `Admin`, `IT`, `Operator`, `Utility`, `ReadOnly`. See [assumptions-and-security.md](assumptions-and-security.md) for what each may do.");

        foreach (var section in rows.GroupBy(r => Section(r.Path)))
        {
            sb.AppendLine();
            sb.AppendLine($"## {Title(section.Key)}");
            sb.AppendLine();
            sb.AppendLine("| Method | Path | Access | Parameters |");
            sb.AppendLine("|---|---|---|---|");
            foreach (var r in section)
                sb.AppendLine($"| {r.Method} | `{r.Path}` | {r.Auth} | {Parameters(r.Api)} |");
        }

        sb.AppendLine();
        await File.WriteAllTextAsync(path, sb.ToString().Replace("\r\n", "\n"), new UTF8Encoding(false));
    }

    private static string Section(string path)
    {
        var parts = path.Trim('/').Split('/');
        return parts.Length >= 3 && parts[0] == "api" ? parts[2] : "platform";
    }

    private static string Title(string section) => section switch
    {
        "platform" => "Platform",
        "auth" => "Authentication",
        "meter-data" => "Meter data",
        "tariff-change-requests" => "Tariff change requests",
        "connectivity-commands" => "Connectivity commands (RC/DC)",
        "meter-commands" => "Meter commands (meter credit)",
        "audit-entries" => "Audit entries",
        "billing-reconciliation" => "Billing reconciliation",
        "reconciliation-adjustments" => "Reconciliation adjustments",
        "calculation-workbench" => "Calculation workbench",
        "risk-indicators" => "Risk indicators",
        "sla" => "SLA",
        "meter-replacements" => "Meter replacements",
        _ => char.ToUpperInvariant(section[0]) + section[1..].Replace('-', ' '),
    };

    private static string Parameters(ApiDescription api)
    {
        var parts = new List<string>();
        foreach (var p in api.ParameterDescriptions)
        {
            var source = p.Source == BindingSource.Path ? "path" : p.Source == BindingSource.Query ? "query" : p.Source == BindingSource.Body ? "body" : p.Source?.Id?.ToLowerInvariant() ?? "?";
            if (source == "body")
            {
                parts.Add("**body** " + BodyShape(p.Type));
                continue;
            }
            if (p.Source == BindingSource.Services || p.Source == BindingSource.Special) continue;
            var type = TypeName(p.Type);
            var optional = source == "query" && !p.IsRequired;
            parts.Add($"{source} `{p.Name}` {type}{(optional ? " (optional)" : "")}");
        }
        return parts.Count == 0 ? "none" : string.Join("<br>", parts);
    }

    /// <summary>Lists a request record's properties, marking those with a default or a nullable type as optional.</summary>
    private static string BodyShape(Type? type)
    {
        if (type is null) return "";
        if (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>)) return $"list of {TypeName(type.GetGenericArguments()[0])}";
        var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
        if (ctor is null || ctor.GetParameters().Length == 0) return $"`{type.Name}`";
        var nullability = new NullabilityInfoContext();
        var fields = ctor.GetParameters().Select(p =>
        {
            var optional = p.HasDefaultValue || nullability.Create(p).ReadState == NullabilityState.Nullable || Nullable.GetUnderlyingType(p.ParameterType) is not null;
            return $"`{Camel(p.Name!)}` {TypeName(p.ParameterType)}{(optional ? "?" : "")}";
        });
        return $"`{type.Name}` {{ {string.Join(", ", fields)} }}";
    }

    private static string Camel(string s) => char.ToLowerInvariant(s[0]) + s[1..];

    private static string TypeName(Type? t)
    {
        if (t is null) return "";
        var underlying = Nullable.GetUnderlyingType(t);
        if (underlying is not null) return TypeName(underlying);
        if (t == typeof(string)) return "string";
        if (t == typeof(Guid)) return "guid";
        if (t == typeof(bool)) return "bool";
        if (t == typeof(int) || t == typeof(long)) return "int";
        if (t == typeof(decimal) || t == typeof(double)) return "number";
        if (t == typeof(DateTime) || t == typeof(DateOnly)) return "date";
        if (t.IsEnum) return $"{t.Name} ({string.Join("|", Enum.GetNames(t))})";
        if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(List<>)) return $"list of {TypeName(t.GetGenericArguments()[0])}";
        if (t.IsGenericType) return $"{t.Name.Split('`')[0]}<{string.Join(", ", t.GetGenericArguments().Select(TypeName))}>";
        return t.Name;
    }
}
