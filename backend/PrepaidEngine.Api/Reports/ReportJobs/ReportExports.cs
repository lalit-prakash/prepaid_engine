using System.Globalization;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Reports.ReportJobs;

/// <summary>The filters a full export can carry: the same date range, status and network hierarchy the report page offers.</summary>
public sealed record ReportJobParameters(
    DateTime? From, DateTime? To, string? Status,
    Guid? ZoneId, Guid? CircleId, Guid? DivisionId, Guid? SubDivisionId, Guid? SubstationId, Guid? FeederId, Guid? DtrId)
{
    public HierarchyQuery Hierarchy => new(ZoneId, CircleId, DivisionId, SubDivisionId, SubstationId, FeederId, DtrId);
}

/// <summary>One row of an export: the keyset position (time, id) and the cells.</summary>
public sealed record ExportRow(DateTime At, Guid Id, string?[] Cells);

/// <summary>
/// The row-level reports that can be exported in full. Each one pages through its data newest first with a keyset cursor,
/// a chunk at a time, so an export of millions of rows never holds more than one chunk in memory. Columns match the
/// report on screen, including the seven network hierarchy levels.
/// </summary>
public static class ReportExports
{
    public const string Billing = "billing";
    public const string RechargeFailures = "recharge-failures";
    public const string MeterCreditFailures = "meter-credit-failures";

    public static readonly string[] Keys = { Billing, RechargeFailures, MeterCreditFailures };

    private static readonly string[] HierarchyHeaders = { "Zone", "Circle", "Division", "Sub-division", "Substation", "Feeder", "DTR" };

    public static string[] Headers(string key) => key switch
    {
        Billing => new[] { "Generated", "Account", "Consumer" }.Concat(HierarchyHeaders)
            .Concat(new[] { "Category", "Tariff used", "Net energy", "Fixed", "Duty", "FPPAS", "Total", "Settled", "Status" }).ToArray(),
        RechargeFailures => new[] { "Initiated", "Account", "Consumer" }.Concat(HierarchyHeaders)
            .Concat(new[] { "Amount", "RMS reference", "Payment" }).ToArray(),
        MeterCreditFailures => new[] { "Created", "Account", "Consumer" }.Concat(HierarchyHeaders)
            .Concat(new[] { "Credit amount", "Status", "Retries", "Response code", "Failure reason" }).ToArray(),
        _ => throw new ArgumentException($"Unknown report '{key}'."),
    };

    /// <summary>The next chunk after the given position (null for the first), at most <paramref name="size"/> rows.</summary>
    public static Task<List<ExportRow>> NextChunkAsync(
        PrepaidEngineDbContext db, string key, ReportJobParameters p, (DateTime At, Guid Id)? after, int size, CancellationToken ct) => key switch
    {
        Billing => BillingChunkAsync(db, p, after, size, ct),
        RechargeFailures => RechargeFailuresChunkAsync(db, p, after, size, ct),
        MeterCreditFailures => MeterCreditFailuresChunkAsync(db, p, after, size, ct),
        _ => throw new ArgumentException($"Unknown report '{key}'."),
    };

    private static (DateTime? Start, DateTime? EndExclusive) Range(ReportJobParameters p) =>
        (p.From.HasValue ? DateTime.SpecifyKind(p.From.Value.Date, DateTimeKind.Utc) : null,
         p.To.HasValue ? DateTime.SpecifyKind(p.To.Value.Date.AddDays(1), DateTimeKind.Utc) : null);

    private static string Money(decimal v) => v.ToString("0.00", CultureInfo.InvariantCulture);
    private static string When(DateTime v) => DateTime.SpecifyKind(v, DateTimeKind.Utc).ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    private static async Task<List<ExportRow>> BillingChunkAsync(PrepaidEngineDbContext db, ReportJobParameters p, (DateTime At, Guid Id)? after, int size, CancellationToken ct)
    {
        var (start, end) = Range(p);
        var query =
            from b in db.Bills.AsNoTracking()
            join c in db.Consumers.AsNoTracking().ByHierarchy(p.Hierarchy) on b.ConsumerId equals c.Id
            join t in db.Tariffs.AsNoTracking() on b.TariffId equals t.Id
            select new { b, c, t };
        if (start.HasValue) query = query.Where(x => x.b.GeneratedAt >= start.Value);
        if (end.HasValue) query = query.Where(x => x.b.GeneratedAt < end.Value);
        if (Enum.TryParse<BillStatus>(p.Status, ignoreCase: true, out var status)) query = query.Where(x => x.b.Status == status);
        if (after is { } a) query = query.Where(x => x.b.GeneratedAt < a.At || (x.b.GeneratedAt == a.At && x.b.Id.CompareTo(a.Id) < 0));

        var rows = await query.OrderByDescending(x => x.b.GeneratedAt).ThenByDescending(x => x.b.Id).Take(size)
            .Select(x => new
            {
                x.b.Id, x.b.GeneratedAt, x.c.AccountNumber, x.c.Name,
                Zone = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Zone.Name,
                Circle = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Name,
                Division = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Name,
                SubDivision = x.c.Dtr!.Feeder.Substation.SubDivision.Name,
                Substation = x.c.Dtr!.Feeder.Substation.Name,
                Feeder = x.c.Dtr!.Feeder.Name,
                Dtr = x.c.Dtr!.Name,
                x.t.Category, TariffName = x.t.Name,
                Net = x.b.EnergyChargeGross - x.b.PrepaidRebateAmount,
                x.b.FixedCharge, x.b.ElectricityDutyAmount, x.b.FppasAmount, x.b.Amount, x.b.AmountPaid, x.b.Status,
            })
            .ToListAsync(ct);

        return rows.Select(r => new ExportRow(r.GeneratedAt, r.Id, new string?[]
        {
            When(r.GeneratedAt), r.AccountNumber, r.Name, r.Zone, r.Circle, r.Division, r.SubDivision, r.Substation, r.Feeder, r.Dtr,
            r.Category.ToString(), r.TariffName, Money(r.Net), Money(r.FixedCharge), Money(r.ElectricityDutyAmount), Money(r.FppasAmount),
            Money(r.Amount), Money(r.AmountPaid), r.Status.ToString(),
        })).ToList();
    }

    private static async Task<List<ExportRow>> RechargeFailuresChunkAsync(PrepaidEngineDbContext db, ReportJobParameters p, (DateTime At, Guid Id)? after, int size, CancellationToken ct)
    {
        var (start, end) = Range(p);
        var query =
            from r in db.RechargeTransactions.AsNoTracking()
            join c in db.Consumers.AsNoTracking().ByHierarchy(p.Hierarchy) on r.ConsumerId equals c.Id
            where r.Status == RechargeStatus.Failed
            select new { r, c };
        if (start.HasValue) query = query.Where(x => x.r.InitiatedAt >= start.Value);
        if (end.HasValue) query = query.Where(x => x.r.InitiatedAt < end.Value);
        if (after is { } a) query = query.Where(x => x.r.InitiatedAt < a.At || (x.r.InitiatedAt == a.At && x.r.Id.CompareTo(a.Id) < 0));

        var rows = await query.OrderByDescending(x => x.r.InitiatedAt).ThenByDescending(x => x.r.Id).Take(size)
            .Select(x => new
            {
                x.r.Id, x.r.InitiatedAt, x.c.AccountNumber, x.c.Name,
                Zone = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Zone.Name,
                Circle = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Name,
                Division = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Name,
                SubDivision = x.c.Dtr!.Feeder.Substation.SubDivision.Name,
                Substation = x.c.Dtr!.Feeder.Substation.Name,
                Feeder = x.c.Dtr!.Feeder.Name,
                Dtr = x.c.Dtr!.Name,
                x.r.Amount, x.r.RmsReferenceId, x.r.Status,
            })
            .ToListAsync(ct);

        return rows.Select(r => new ExportRow(r.InitiatedAt, r.Id, new string?[]
        {
            When(r.InitiatedAt), r.AccountNumber, r.Name, r.Zone, r.Circle, r.Division, r.SubDivision, r.Substation, r.Feeder, r.Dtr,
            Money(r.Amount), r.RmsReferenceId, r.Status.ToString(),
        })).ToList();
    }

    private static async Task<List<ExportRow>> MeterCreditFailuresChunkAsync(PrepaidEngineDbContext db, ReportJobParameters p, (DateTime At, Guid Id)? after, int size, CancellationToken ct)
    {
        var (start, end) = Range(p);
        var query =
            from m in db.MeterCommands.AsNoTracking()
            join c in db.Consumers.AsNoTracking().ByHierarchy(p.Hierarchy) on m.ConsumerId equals c.Id
            where m.Status == MeterCommandStatus.Failed || m.Status == MeterCommandStatus.TimedOut
            select new { m, c };
        if (start.HasValue) query = query.Where(x => x.m.CreatedAt >= start.Value);
        if (end.HasValue) query = query.Where(x => x.m.CreatedAt < end.Value);
        if (after is { } a) query = query.Where(x => x.m.CreatedAt < a.At || (x.m.CreatedAt == a.At && x.m.Id.CompareTo(a.Id) < 0));

        var rows = await query.OrderByDescending(x => x.m.CreatedAt).ThenByDescending(x => x.m.Id).Take(size)
            .Select(x => new
            {
                x.m.Id, x.m.CreatedAt, x.c.AccountNumber, x.c.Name,
                Zone = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Zone.Name,
                Circle = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Name,
                Division = x.c.Dtr!.Feeder.Substation.SubDivision.Division.Name,
                SubDivision = x.c.Dtr!.Feeder.Substation.SubDivision.Name,
                Substation = x.c.Dtr!.Feeder.Substation.Name,
                Feeder = x.c.Dtr!.Feeder.Name,
                Dtr = x.c.Dtr!.Name,
                x.m.CreditAmount, x.m.Status, x.m.RetryCount, x.m.ResponseCode, x.m.ErrorMessage,
            })
            .ToListAsync(ct);

        return rows.Select(r => new ExportRow(r.CreatedAt, r.Id, new string?[]
        {
            When(r.CreatedAt), r.AccountNumber, r.Name, r.Zone, r.Circle, r.Division, r.SubDivision, r.Substation, r.Feeder, r.Dtr,
            Money(r.CreditAmount), r.Status.ToString(), r.RetryCount.ToString(CultureInfo.InvariantCulture), r.ResponseCode, r.ErrorMessage,
        })).ToList();
    }
}
