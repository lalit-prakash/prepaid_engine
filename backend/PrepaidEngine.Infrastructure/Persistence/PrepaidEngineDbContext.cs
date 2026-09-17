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
    public DbSet<FppasCharge> FppasCharges => Set<FppasCharge>();
    public DbSet<MeterCommand> MeterCommands => Set<MeterCommand>();
    public DbSet<ConnectivityCommand> ConnectivityCommands => Set<ConnectivityCommand>();
    public DbSet<ConversionRequest> ConversionRequests => Set<ConversionRequest>();
    public DbSet<ReverseConversionRequest> ReverseConversionRequests => Set<ReverseConversionRequest>();
    public DbSet<PaymentModeChangeCommand> PaymentModeChangeCommands => Set<PaymentModeChangeCommand>();
    public DbSet<ReconciliationAdjustment> ReconciliationAdjustments => Set<ReconciliationAdjustment>();
    public DbSet<OperationalException> OperationalExceptions => Set<OperationalException>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<TariffVersion> TariffVersions => Set<TariffVersion>();
    public DbSet<DailyLoadProfile> DailyLoadProfiles => Set<DailyLoadProfile>();
    public DbSet<BillingRun> BillingRuns => Set<BillingRun>();
    public DbSet<MeterAssignment> MeterAssignments => Set<MeterAssignment>();
    public DbSet<NotificationEvent> NotificationEvents => Set<NotificationEvent>();
    public DbSet<MeterBillingControl> MeterBillingControls => Set<MeterBillingControl>();

    // --- MDMS data foundation: BP (register validation), LS (consumption intelligence), IP
    // (instantaneous meter health), Events/Alarms, and cross-source energy validation. DLP above
    // remains the sole daily billing driver — see each entity's own doc comment for its role.
    public DbSet<RegisterReading> RegisterReadings => Set<RegisterReading>();
    public DbSet<LoadSurveyInterval> LoadSurveyIntervals => Set<LoadSurveyInterval>();
    public DbSet<InstantaneousReading> InstantaneousReadings => Set<InstantaneousReading>();
    public DbSet<MeterEvent> MeterEvents => Set<MeterEvent>();
    public DbSet<MeterAlarm> MeterAlarms => Set<MeterAlarm>();
    public DbSet<EnergyValidationResult> EnergyValidationResults => Set<EnergyValidationResult>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PrepaidEngineDbContext).Assembly);
    }
}
