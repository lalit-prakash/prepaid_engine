using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Infrastructure.Connectivity;

/// <summary>
/// Disconnects the consumers whose credit ran out while the credit hours were on. The tariff book (22.4) keeps supply on from 4:00 PM to 11:00 AM
/// whatever the balance, so the emergency credit guard leaves such a consumer connected outside 11:00 AM to 4:00 PM; once the window opens this
/// finds the connected prepaid consumers whose balance is beyond their emergency credit and hands each to the guard. It does nothing while the
/// window is closed, and does nothing for anyone who has recharged since (they are no longer beyond the limit).
/// </summary>
public class DeferredDisconnectionService
{
    private const int BatchSize = 200;

    private readonly PrepaidEngineDbContext _db;
    private readonly IEmergencyCreditGuard _guard;

    public DeferredDisconnectionService(PrepaidEngineDbContext db, IEmergencyCreditGuard guard)
    {
        _db = db;
        _guard = guard;
    }

    /// <summary>Returns how many consumers were handed to the guard (zero when the window is closed).</summary>
    public async Task<int> RunAsync(DateTime utcNow, CancellationToken cancellationToken = default)
    {
        if (!DisconnectionWindow.IsOpen(utcNow)) return 0;

        var handled = 0;
        Guid? cursor = null;
        while (true)
        {
            var batch = await _db.Consumers
                .Include(c => c.Wallet)
                .Where(c => c.BillingMode == BillingMode.Prepaid && c.ConnectionStatus == ConnectionStatus.Active
                    && c.Wallet.Balance < -c.Wallet.EmergencyCreditLimit && (cursor == null || c.Id.CompareTo(cursor.Value) > 0))
                .OrderBy(c => c.Id)
                .Take(BatchSize)
                .ToListAsync(cancellationToken);
            if (batch.Count == 0) break;

            foreach (var consumer in batch)
            {
                await _guard.EvaluateAsync(consumer, cancellationToken);
                handled++;
            }
            await _db.SaveChangesAsync(cancellationToken);
            _db.ChangeTracker.Clear();
            cursor = batch[^1].Id;
        }
        return handled;
    }
}
