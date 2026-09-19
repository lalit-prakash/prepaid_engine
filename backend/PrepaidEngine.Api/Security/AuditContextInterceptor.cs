using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Api.Security;

/// <summary>
/// Stamps every audit entry being saved with the signed-in user's role, the client address and the request's
/// correlation id, in one place, so each of the many call sites that record an audit entry does not have to.
/// Entries written outside a web request (background workers) get no context, which reads as a system action.
/// </summary>
public sealed class AuditContextInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _accessor;

    public AuditContextInterceptor(IHttpContextAccessor accessor) => _accessor = accessor;

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Stamp(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Stamp(eventData.Context);
        return ValueTask.FromResult(result);
    }

    private void Stamp(DbContext? context)
    {
        var http = _accessor.HttpContext;
        if (context is null || http is null) return;

        var role = http.User.FindFirstValue(ClaimTypes.Role);
        var ip = http.Connection.RemoteIpAddress?.ToString();
        var correlationId = http.Items.TryGetValue(Correlation.ItemKey, out var c) ? c as string : null;

        foreach (var entry in context.ChangeTracker.Entries<AuditEntry>().Where(e => e.State == EntityState.Added))
            entry.Entity.AttachContext(role, ip, correlationId);
    }
}

/// <summary>Correlation id handling: one id per request, taken from a well-formed incoming <c>X-Correlation-Id</c> header or generated.</summary>
public static class Correlation
{
    public const string HeaderName = "X-Correlation-Id";
    public const string ItemKey = "CorrelationId";

    /// <summary>Accepts only short, plain ids so a client cannot smuggle odd characters into logs or the audit table.</summary>
    public static bool IsAcceptable(string? value)
        => !string.IsNullOrEmpty(value) && value.Length is >= 8 and <= 64 && value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch == '-');

    public static string Resolve(string? incoming) => IsAcceptable(incoming) ? incoming! : Guid.NewGuid().ToString("N");
}
