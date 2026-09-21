using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
    public DbSet<ReportJob> ReportJobs => Set<ReportJob>();
    public DbSet<DailyWalletStat> DailyWalletStats => Set<DailyWalletStat>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<Circle> Circles => Set<Circle>();
    public DbSet<Division> Divisions => Set<Division>();
    public DbSet<SubDivision> SubDivisions => Set<SubDivision>();
    public DbSet<Substation> Substations => Set<Substation>();
    public DbSet<Feeder> Feeders => Set<Feeder>();
    public DbSet<Dtr> Dtrs => Set<Dtr>();
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
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<SystemSetting> SystemSettings => Set<SystemSetting>();
    public DbSet<PasswordResetRequest> PasswordResetRequests => Set<PasswordResetRequest>();
    public DbSet<UserPasswordOverride> UserPasswordOverrides => Set<UserPasswordOverride>();
    public DbSet<TariffVersion> TariffVersions => Set<TariffVersion>();
    public DbSet<TariffChangeRequest> TariffChangeRequests => Set<TariffChangeRequest>();
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
        // The consumer search uses case-insensitive prefix and contains matching (ILIKE), which ordinary
        // b-tree indexes cannot serve. Trigram indexes (see the Consumers and Meters configurations) can.
        modelBuilder.HasPostgresExtension("pg_trgm");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PrepaidEngineDbContext).Assembly);
    }

    /// <summary>
    /// Every DateTime is stored and read as UTC. Npgsql refuses to write a DateTime whose Kind is
    /// Unspecified to a timestamptz column, and JSON or query-string dates without an offset (for
    /// example "2026-09-18") bind as Unspecified, which used to surface as a 500 on any endpoint
    /// that accepted one. Normalising here fixes the whole class of problem in one place.
    /// </summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
    };

    private sealed class UtcDateTimeConverter : ValueConverter<DateTime, DateTime>
    {
        public UtcDateTimeConverter()
            : base(v => ToUtc(v), v => DateTime.SpecifyKind(v, DateTimeKind.Utc))
        {
        }
    }

    private sealed class NullableUtcDateTimeConverter : ValueConverter<DateTime?, DateTime?>
    {
        public NullableUtcDateTimeConverter()
            : base(
                v => v.HasValue ? ToUtc(v.Value) : v,
                v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v)
        {
        }
    }
}
