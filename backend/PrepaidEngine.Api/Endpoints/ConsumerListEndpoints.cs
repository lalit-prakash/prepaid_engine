using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Reports;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Endpoints;

/// <summary>
/// The Consumers screen: a keyset-paged search (by account number, newest-first is not needed, so it pages on the unique account
/// number), database-side counts for the postpaid-to-prepaid conversion cards, and an Excel download of the same filtered list.
///
/// Conversion: a consumer has "converted" when a conversion request for them is Completed. <c>conversion</c> narrows to consumers with
/// a request in that state: Completed, Pending (requested or approved) or Rejected. The conversion date shown is the conversion date of
/// the latest Completed request. From/To (conversion date) keep only consumers who have a request in the chosen state (any state when none
/// is chosen) dated in that range. The consumer number filter matches account numbers by prefix only, never names.
/// </summary>
public static class ConsumerListEndpoints
{
    private const int MaxPage = 100;
    private const int ExportMaxRows = 100_000;

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
            query = query.Where(c => db.ConversionRequests.Any(r => r.ConsumerId == c.Id
                && (conversion == "Completed" ? r.Status == ConversionStatus.Completed
                    : conversion == "Pending" ? r.Status == ConversionStatus.Requested || r.Status == ConversionStatus.Approved
                    : conversion == "Rejected" ? r.Status == ConversionStatus.Rejected
                    : true)
                && (start == null || r.ConversionDate >= start) && (endExclusive == null || r.ConversionDate < endExclusive)));
        }
        return query;
    }

    private static IQueryable<ConsumerListRow> Rows(PrepaidEngineDbContext db, IQueryable<Consumer> query) => query.Select(c => new ConsumerListRow
    {
        AccountNumber = c.AccountNumber,
        Name = c.Name,
        MobileNumber = c.MobileNumber,
        ConnectionStatus = c.ConnectionStatus,
        MeterNumber = c.Meter.MeterNumber,
        WalletBalance = c.Wallet.Balance,
        ConversionDate = db.ConversionRequests.Where(r => r.ConsumerId == c.Id && r.Status == ConversionStatus.Completed).Max(r => (DateTime?)r.ConversionDate),
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
        public DateTime? ConversionDate { get; set; }
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

            var rows = await Rows(db, query.OrderBy(c => c.AccountNumber).Take(size + 1)).ToListAsync();
            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? items[^1].AccountNumber : null, totalCount });
        })
        .WithName("SearchConsumers")
        .RequireAuthorization();

        // Counts for the conversion cards, over every consumer (a consumer counts once per state).
        app.MapGet("/api/v1/consumers/summary", async (PrepaidEngineDbContext db) =>
        {
            var total = await db.Consumers.AsNoTracking().CountAsync();
            var requests = db.ConversionRequests.AsNoTracking();
            var converted = await requests.Where(r => r.Status == ConversionStatus.Completed).Select(r => r.ConsumerId).Distinct().CountAsync();
            var pending = await requests.Where(r => r.Status == ConversionStatus.Requested || r.Status == ConversionStatus.Approved).Select(r => r.ConsumerId).Distinct().CountAsync();
            var rejected = await requests.Where(r => r.Status == ConversionStatus.Rejected).Select(r => r.ConsumerId).Distinct().CountAsync();
            return Results.Ok(new { Total = total, Converted = converted, Pending = pending, Rejected = rejected });
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

            var rows = await Rows(db, query.OrderBy(c => c.AccountNumber)).ToListAsync();
            var offset = TimeSpan.FromMinutes(Math.Clamp(tzOffsetMinutes ?? 0, -840, 840));
            static string? Text(DateTime? v, TimeSpan o, string format) => v.HasValue ? (DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) + o).ToString(format, System.Globalization.CultureInfo.InvariantCulture) : null;
            var bytes = XlsxWriter.Build("Consumers", new[]
            {
                "Zone", "Circle", "Division", "Subdivision", "Sub Station", "Feeder", "Feeder code", "DTR", "DTR Code", "Consumer Number",
                "Consumer Name", "Mobile", "MSN", "Connection", "Wallet Balance", "Conversion Date", "Last Recharge",
            }, rows.Select(r => new string?[]
            {
                r.Zone, r.Circle, r.Division, r.SubDivision, r.Substation, r.Feeder, r.FeederCode, r.Dtr, r.DtrCode, r.AccountNumber,
                r.Name, r.MobileNumber, r.MeterNumber, r.ConnectionStatus.ToString(), r.WalletBalance.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture),
                r.ConversionDate.HasValue ? r.ConversionDate.Value.ToString("dd-MM-yyyy", System.Globalization.CultureInfo.InvariantCulture) : null,
                Text(r.LastRechargeAt, offset, "dd-MM-yyyy HH:mm:ss"),
            }));
            return Results.File(bytes, XlsxWriter.ContentType, $"consumers-{DateTime.UtcNow:yyyyMMdd-HHmm}.xlsx");
        })
        .WithName("ExportConsumers")
        .RequireAuthorization();
    }
}
