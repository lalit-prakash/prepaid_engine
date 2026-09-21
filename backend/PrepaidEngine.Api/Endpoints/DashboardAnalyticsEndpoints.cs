using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Api.Endpoints;

/// <summary>
/// The two analytics boxes on the dashboard, each a handful of counts computed in the database.
///
/// Meter communication looks at the meters currently installed at a consumer and at when data from each last reached the engine
/// (<c>Meters.LastCommunicatedAt</c>, kept up to date on ingestion): communicating = heard from within the last 3 days; not communicating from
/// 3 days / 7 days = last heard from at least that long ago (the 7-day group is inside the 3-day group); never communicated = no data has ever
/// arrived. Wallet balance distribution counts consumers by current wallet balance in fixed rupee bands.
/// </summary>
public static class DashboardAnalyticsEndpoints
{
    public static readonly (string Label, decimal? Min, decimal? Max)[] WalletBands =
    {
        ("Below ₹0", null, 0m),
        ("₹0 – ₹100", 0m, 100m),
        ("₹100 – ₹500", 100m, 500m),
        ("₹500 – ₹1,000", 500m, 1000m),
        ("₹1,000 – ₹5,000", 1000m, 5000m),
        ("₹5,000 and above", 5000m, null),
    };

    public static void MapDashboardAnalyticsEndpoints(this WebApplication app)
    {
        app.MapGet("/api/v1/analytics/meter-communication", async (PrepaidEngineDbContext db) =>
        {
            var now = DateTime.UtcNow;
            var threeDays = now.AddDays(-3);
            var sevenDays = now.AddDays(-7);

            var counts = await db.Consumers.AsNoTracking()
                .Select(c => c.Meter)
                .GroupBy(_ => 1)
                .Select(g => new
                {
                    Total = g.Count(),
                    Communicating = g.Count(m => m.LastCommunicatedAt >= threeDays),
                    NonCommunicating3Days = g.Count(m => m.LastCommunicatedAt != null && m.LastCommunicatedAt < threeDays),
                    NonCommunicating7Days = g.Count(m => m.LastCommunicatedAt != null && m.LastCommunicatedAt < sevenDays),
                    NeverCommunicated = g.Count(m => m.LastCommunicatedAt == null),
                })
                .FirstOrDefaultAsync();

            return Results.Ok(new
            {
                AsOf = now,
                Total = counts?.Total ?? 0,
                Communicating = counts?.Communicating ?? 0,
                NonCommunicating3Days = counts?.NonCommunicating3Days ?? 0,
                NonCommunicating7Days = counts?.NonCommunicating7Days ?? 0,
                NeverCommunicated = counts?.NeverCommunicated ?? 0,
            });
        })
        .WithName("MeterCommunicationAnalytics")
        .RequireAuthorization();

        app.MapGet("/api/v1/analytics/wallet-distribution", async (PrepaidEngineDbContext db) =>
        {
            var grouped = await db.Consumers.AsNoTracking()
                .GroupBy(c =>
                    c.Wallet.Balance < 0m ? 0 :
                    c.Wallet.Balance < 100m ? 1 :
                    c.Wallet.Balance < 500m ? 2 :
                    c.Wallet.Balance < 1000m ? 3 :
                    c.Wallet.Balance < 5000m ? 4 : 5)
                .Select(g => new { Band = g.Key, Count = g.Count(), Total = g.Sum(c => c.Wallet.Balance) })
                .ToListAsync();

            var bands = WalletBands.Select((b, i) =>
            {
                var g = grouped.FirstOrDefault(x => x.Band == i);
                return new { b.Label, Count = g?.Count ?? 0, TotalBalance = g?.Total ?? 0m };
            }).ToList();
            return Results.Ok(new { Total = bands.Sum(b => b.Count), Bands = bands });
        })
        .WithName("WalletDistributionAnalytics")
        .RequireAuthorization();
    }
}
