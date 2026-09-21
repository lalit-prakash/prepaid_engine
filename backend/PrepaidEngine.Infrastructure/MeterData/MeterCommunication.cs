using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Infrastructure.MeterData;

/// <summary>Records that a meter has just been heard from. One conditional UPDATE that only moves the time forward, so concurrent
/// ingestion (or a late, out-of-order message) can never make a meter look older than it is.</summary>
public static class MeterCommunication
{
    public static Task TouchAsync(PrepaidEngineDbContext db, Guid meterId, DateTime receivedAtUtc, CancellationToken cancellationToken = default)
        => db.Meters
            .Where(m => m.Id == meterId && (m.LastCommunicatedAt == null || m.LastCommunicatedAt < receivedAtUtc))
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.LastCommunicatedAt, receivedAtUtc), cancellationToken);
}
