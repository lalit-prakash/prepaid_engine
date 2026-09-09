using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain.Entities;

namespace PrepaidEngine.Infrastructure.Persistence;

/// <summary>
/// EF Core context for the Prepaid Engine's own persistence: consumers, meters, tariffs,
/// prepaid wallets/ledger, bills and recharge records.
///
/// NOTE (financial boundary): the wallet balance and ledger persisted here are the Prepaid
/// Engine's own working state used for billing/recharge orchestration. RMS remains the
/// authoritative system of record for the consumer's real financial wallet; this context
/// does not attempt to replace it.
/// </summary>
public class PrepaidEngineDbContext : DbContext
{
    public PrepaidEngineDbContext(DbContextOptions<PrepaidEngineDbContext> options)
        : base(options)
    {
    }

    public DbSet<Consumer> Consumers => Set<Consumer>();
    public DbSet<SmartMeter> Meters => Set<SmartMeter>();
    public DbSet<ConsumptionReading> ConsumptionReadings => Set<ConsumptionReading>();
    public DbSet<Tariff> Tariffs => Set<Tariff>();
    public DbSet<PrepaidWallet> Wallets => Set<PrepaidWallet>();
    public DbSet<WalletTransaction> WalletTransactions => Set<WalletTransaction>();
    public DbSet<PrepaidBill> Bills => Set<PrepaidBill>();
    public DbSet<RechargeTransaction> RechargeTransactions => Set<RechargeTransaction>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PrepaidEngineDbContext).Assembly);
    }
}
