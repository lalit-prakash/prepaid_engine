using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Reports;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Endpoints;

/// <summary>
/// The Consumers screen: a keyset-paged search (paged on the unique account number), database-side counts of postpaid-to-prepaid
/// conversion requests, and an Excel download of the same filtered list.
///
/// <c>conversion</c> is the view: Total (consumers with any conversion request), Completed, or Failed (a rejected request; its decision note
/// is the reason). Each row carries the latest matching request's requested time, the latest completed request's converted time, and the
/// latest rejected request's reason. From/To filter on the view's own date: requested time for Total and Failed, converted time for Completed.
/// The consumer number filter matches account numbers by prefix only, never names.
/// </summary>
public static class ConsumerListEndpoints
{
    private const int MaxPage = 100;
    private const int ExportMaxRows = 100_000;

    /// <summary>The last column of the download follows the view: requested time, converted time or failure reason (blank header for the plain list).</summary>
    private static string ViewHeader(string? conversion) => conversion switch { "Completed" => "Converted Date", "Failed" => "Failure Reason", "Total" => "Requested Date", _ => "" };

    private static string Prefix(string term) => term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";

    private static IQueryable<Consumer> Filter(
        PrepaidEngineDbContext db, string? consumerNumber, string? meterNumber, string? q, ConnectionStatus? status, string? conversion,
        DateTime? from, DateTime? to, HierarchyQuery h)
    {
        var query = db.Consumers.AsNoTracking().ByHierarchy(h);

        if (!string.IsNullOrWhiteSpace(consumerNumber)) { var p = Prefix(consumerNumber); query = query.Where(c => EF.Functions.ILike(c.AccountNumber, p, "\\")); }
        if (!string.IsNullOrWhiteSpace(meterNumber)) { var p = Prefix(meterNumber); query = query.Where(c => EF.Functions.ILike(c.Meter.MeterNumber, p, "\\")); }
        if (!string.IsNullOrWhiteSpace(q))
        {
            var prefix = Prefix(q);
            var contains = "%" + prefix;
            query = query.Where(c =>
                EF.Functions.ILike(c.AccountNumber, prefix, "\\") || EF.Functions.ILike(c.Meter.MeterNumber, prefix, "\\") ||
                (c.MobileNumber != null && EF.Functions.ILike(c.MobileNumber, prefix, "\\")) || EF.Functions.ILike(c.Name, contains, "\\"));
        }
        if (status.HasValue) query = query.Where(c => c.ConnectionStatus == status.Value);

        var start = from.HasValue ? DateTime.SpecifyKind(from.Value.Date, DateTimeKind.Utc) : (DateTime?)null;
        var endExclusive = to.HasValue ? DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc) : (DateTime?)null;
        if (!string.IsNullOrEmpty(conversion) || start.HasValue || endExclusive.HasValue)
        {
            var completed = conversion == "Completed";
            query = query.Where(c => db.ConversionRequests.Any(r => r.ConsumerId == c.Id
                && (conversion == "Completed" ? r.Status == ConversionStatus.Completed : conversion == "Failed" ? r.Status == ConversionStatus.Rejected : true)
                && (start == null || (completed ? r.CompletedAt >= start : r.RequestedAt >= start))
                && (endExclusive == null || (completed ? r.CompletedAt < endExclusive : r.RequestedAt < endExclusive))));
        }
        return query;
    }

    private static IQueryable<ConsumerListRow> Rows(PrepaidEngineDbContext db, IQueryable<Consumer> query, string? conversion) => query.Select(c => new ConsumerListRow
    {
        AccountNumber = c.AccountNumber,
        Name = c.Name,
        MobileNumber = c.MobileNumber,
        ConnectionStatus = c.ConnectionStatus,
        MeterNumber = c.Meter.MeterNumber,
        WalletBalance = c.Wallet.Balance,
        RequestedAt = db.ConversionRequests
            .Where(r => r.ConsumerId == c.Id && (conversion == "Completed" ? r.Status == ConversionStatus.Completed : conversion == "Failed" ? r.Status == ConversionStatus.Rejected : true))
            .Max(r => (DateTime?)r.RequestedAt),
        ConvertedAt = db.ConversionRequests.Where(r => r.ConsumerId == c.Id && r.Status == ConversionStatus.Completed).Max(r => r.CompletedAt),
        FailureReason = db.ConversionRequests.Where(r => r.ConsumerId == c.Id && r.Status == ConversionStatus.Rejected)
            .OrderByDescending(r => r.RequestedAt).Select(r => r.DecisionNote).FirstOrDefault(),
        LastRechargeAt = db.RechargeTransactions.Where(r => r.ConsumerId == c.Id && r.Status == RechargeStatus.Success).Max(r => r.CompletedAt),
        Zone = c.Dtr != null ? c.Dtr.Feeder.Substation.SubDivision.Division.Circle.Zone.Name : null,
        Circle = c.Dtr != null ? c.Dtr.Feeder.Substation.SubDivision.Division.Circle.Name : null,
        Division = c.Dtr != null ? c.Dtr.Feeder.Substation.SubDivision.Division.Name : null,
        SubDivision = c.Dtr != null ? c.Dtr.Feeder.Substation.SubDivision.Name : null,
        Substation = c.Dtr != null ? c.Dtr.Feeder.Substation.Name : null,
        Feeder = c.Dtr != null ? c.Dtr.Feeder.Name : null,
        FeederCode = c.Dtr != null ? c.Dtr.Feeder.Code : null,
        Dtr = c.Dtr != null ? c.Dtr.Name : null,
        DtrCode = c.Dtr != null ? c.Dtr.Code : null,
    });

    public sealed class ConsumerListRow
    {
        public string AccountNumber { get; set; } = "";
        public string Name { get; set; } = "";
        public string? MobileNumber { get; set; }
        public ConnectionStatus ConnectionStatus { get; set; }
        public string MeterNumber { get; set; } = "";
        public decimal WalletBalance { get; set; }
        public DateTime? RequestedAt { get; set; }
        public DateTime? ConvertedAt { get; set; }
        public string? FailureReason { get; set; }
        public DateTime? LastRechargeAt { get; set; }
        public string? Zone { get; set; }
        public string? Circle { get; set; }
        public string? Division { get; set; }
        public string? SubDivision { get; set; }
        public string? Substation { get; set; }
        public string? Feeder { get; set; }
        public string? FeederCode { get; set; }
        public string? Dtr { get; set; }
        public string? DtrCode { get; set; }
    }

    public static void MapConsumerListEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/consumers/search", async (
            string? consumerNumber, string? meterNumber, string? q, ConnectionStatus? status, string? conversion, DateTime? from, DateTime? to,
            [AsParameters] HierarchyQuery h, string? after, int? pageSize, PrepaidEngineDbContext db) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, MaxPage);
            var query = Filter(db, consumerNumber, meterNumber, q, status, conversion, from, to, h);
            var totalCount = await query.CountAsync();
            if (!string.IsNullOrEmpty(after)) query = query.Where(c => string.Compare(c.AccountNumber, after) > 0);

            var rows = await Rows(db, query.OrderBy(c => c.AccountNumber).Take(size + 1), conversion).ToListAsync();
            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? items[^1].AccountNumber : null, totalCount });
        })
        .WithName("SearchConsumers")
        .RequireAuthorization();

        // Counts of conversion requests for the cards. Success rate is completed of the requests that have been decided (completed or rejected).
        app.MapGet("/api/v1/consumers/summary", async (PrepaidEngineDbContext db) =>
        {
            var byStatus = await db.ConversionRequests.AsNoTracking().GroupBy(r => r.Status).Select(g => new { Status = g.Key, Count = g.Count() }).ToListAsync();
            int N(ConversionStatus st) => byStatus.FirstOrDefault(x => x.Status == st)?.Count ?? 0;
            return Results.Ok(new
            {
                TotalRequests = byStatus.Sum(x => x.Count),
                Completed = N(ConversionStatus.Completed),
                Failed = N(ConversionStatus.Rejected),
                Pending = N(ConversionStatus.Requested) + N(ConversionStatus.Approved),
            });
        })
        .WithName("ConsumerSummary")
        .RequireAuthorization();

        // Excel download of the filtered list. Refused (rather than cut short) when more than the cap match, so a file is always complete.
        app.MapGet("/api/v1/consumers/export", async (
            string? consumerNumber, string? meterNumber, string? q, ConnectionStatus? status, string? conversion, DateTime? from, DateTime? to,
            [AsParameters] HierarchyQuery h, int? tzOffsetMinutes, PrepaidEngineDbContext db) =>
        {
            var query = Filter(db, consumerNumber, meterNumber, q, status, conversion, from, to, h);
            var count = await query.CountAsync();
            if (count == 0) return Results.BadRequest(new { error = "No consumers match these filters." });
            if (count > ExportMaxRows) return Results.BadRequest(new { error = $"{count:N0} consumers match. Narrow the filters (for example by division or subdivision) to at most {ExportMaxRows:N0} to download." });

            var rows = await Rows(db, query.OrderBy(c => c.AccountNumber), conversion).ToListAsync();
            var offset = TimeSpan.FromMinutes(Math.Clamp(tzOffsetMinutes ?? 0, -840, 840));
            static string? Text(DateTime? v, TimeSpan o, string format) => v.HasValue ? (DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) + o).ToString(format, System.Globalization.CultureInfo.InvariantCulture) : null;
            var bytes = XlsxWriter.Build("Consumers", new[]
            {
                "Zone", "Circle", "Division", "Subdivision", "Sub Station", "Feeder", "Feeder code", "DTR", "DTR Code", "Consumer Number",
                "Consumer Name", "Mobile", "MSN", "Connection", "Wallet Balance", "Last Recharge", ViewHeader(conversion),
            }, rows.Select(r => new string?[]
            {
                r.Zone, r.Circle, r.Division, r.SubDivision, r.Substation, r.Feeder, r.FeederCode, r.Dtr, r.DtrCode, r.AccountNumber,
                r.Name, r.MobileNumber, r.MeterNumber, r.ConnectionStatus.ToString(), r.WalletBalance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                Text(r.LastRechargeAt, offset, "dd-MM-yyyy HH:mm:ss"),
                conversion == "Completed" ? Text(r.ConvertedAt, offset, "dd-MM-yyyy HH:mm:ss") : conversion == "Failed" ? r.FailureReason ?? "No reason recorded" : conversion == "Total" ? Text(r.RequestedAt, offset, "dd-MM-yyyy HH:mm:ss") : null,
            }));
            return Results.File(bytes, XlsxWriter.ContentType, $"consumers-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
        })
        .WithName("ExportConsumers")
        .RequireAuthorization();
    }
}
