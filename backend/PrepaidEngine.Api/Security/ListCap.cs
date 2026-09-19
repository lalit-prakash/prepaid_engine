using Microsoft.EntityFrameworkCore;

namespace PrepaidEngine.Api.Security;

/// <summary>
/// Hard ceiling on list endpoints that have no paging of their own. A response never holds more than
/// <see cref="MaxRows"/> rows; when more exist the API says so in the <c>X-Result-Truncated</c> header instead
/// of silently returning a partial list. Pages that need every row must use the paged search endpoints.
/// </summary>
public static class ListCap
{
    public const int MaxRows = 1000;
    public const string TruncatedHeader = "X-Result-Truncated";

    public static async Task<List<T>> ToCappedListAsync<T>(this IQueryable<T> query, HttpContext http)
    {
        var rows = await query.Take(MaxRows + 1).ToListAsync();
        if (rows.Count <= MaxRows) return rows;
        http.Response.Headers[TruncatedHeader] = "true";
        rows.RemoveAt(rows.Count - 1);
        return rows;
    }
}
