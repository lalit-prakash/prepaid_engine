using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using PrepaidEngine.Application.Wallets;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Infrastructure.Wallets;

/// <summary>
/// Records the day's wallet totals (see <see cref="DailyWalletStat"/>). The counts are computed by the database in one
/// grouped query, with the same low-balance definition the dashboard uses, and written to the day's row, which is
/// created on first use and overwritten by later readings the same day. Safe to run on several instances: the date is
/// the key, so two writers can only ever produce one row.
/// </summary>
public class WalletStatsService
{
    private readonly PrepaidEngineDbContext _db;
    private readonly LowBalanceOptions _lowBalance;

    public WalletStatsService(PrepaidEngineDbContext db, IOptions<LowBalanceOptions> lowBalance)
    {
        _db = db;
        _lowBalance = lowBalance.Value;
    }

    public async Task<DailyWalletStat> RecordAsync(DateOnly date, DateTime nowUtc, CancellationToken cancellationToken = default)
    {
        decimal? threshold = _lowBalance.ThresholdRs; // null: below each wallet's own emergency credit limit
        var totals = await _db.Consumers.AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Total = g.Count(),
                Active = g.Count(c => c.ConnectionStatus == ConnectionStatus.Active),
                Disconnected = g.Count(c => c.ConnectionStatus == ConnectionStatus.Disconnected),
                Low = g.Count(c => c.Wallet.Balance < (threshold ?? c.Wallet.EmergencyCreditLimit)),
                // Summed as a double so the same query also runs on SQLite (which cannot sum decimals); a display total
                // stays exact to the paisa well past Rs.10 billion, and is rounded back to two places below.
                Wallet = g.Sum(c => (double)c.Wallet.Balance),
            })
            .FirstOrDefaultAsync(cancellationToken);

        for (var attempt = 0; ; attempt++)
        {
            var stat = await _db.DailyWalletStats.FirstOrDefaultAsync(s => s.Date == date, cancellationToken);
            var isNew = stat is null;
            stat ??= new DailyWalletStat(date);
            stat.Record(totals?.Total ?? 0, totals?.Active ?? 0, totals?.Disconnected ?? 0, totals?.Low ?? 0, Math.Round((decimal)(totals?.Wallet ?? 0d), 2), nowUtc);
            if (isNew) _db.DailyWalletStats.Add(stat);
            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                _db.ChangeTracker.Clear();
                return stat;
            }
            catch (DbUpdateException) when (isNew && attempt == 0)
            {
                _db.ChangeTracker.Clear(); // another instance created the day's row first: update it instead
            }
        }
    }
}
