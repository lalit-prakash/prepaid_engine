using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A smart-meter consumer enrolled in the prepaid billing scheme.
/// </summary>
public class Consumer
{
    public Guid Id { get; private set; }
    public string AccountNumber { get; private set; }
    public string Name { get; private set; }
    public string ServiceAddress { get; private set; }
    public ConnectionStatus ConnectionStatus { get; private set; }
    public BillingMode BillingMode { get; private set; }
    public SmartMeter Meter { get; private set; }
    public PrepaidWallet Wallet { get; private set; }

    /// <summary>Connected load (kW) or contract demand (kVA), used to compute the tariff's fixed/demand charge.</summary>
    public decimal ConnectedLoadKw { get; private set; }

    /// <summary>True for a NET (bidirectional, e.g. rooftop solar) meter. Per the AMISP
    /// integration requirement doc section 1 ("If the consumer is NET meter consumer, then such
    /// meter shall not be converted to prepaid"), a NET-metered consumer must never go through
    /// <see cref="ConvertToPrepaid"/> — enforced at the conversion endpoint, not here, since
    /// rejecting a request needs to happen without throwing out of a state-changing method.
    /// A fixed physical fact recorded at enrollment — no setter, no domain reason to change it.</summary>
    public bool IsNetMeter { get; private set; }

    /// <summary>The tariff automated LS/DLP billing uses for this consumer. A consumer without a
    /// tariff is never silently billed against an arbitrary default — LS/DLP processing records
    /// an exception instead (see the billing engine). Assigned via <see cref="AssignTariff"/>,
    /// never a raw property set.</summary>
    public Guid? TariffId { get; private set; }

    /// <summary>Notification destination. Deliberately consumer-level, not meter-level — a meter
    /// can be replaced while the consumer/service point (and their phone number) stays the
    /// same.</summary>
    public string? MobileNumber { get; private set; }

    /// <summary>The distribution transformer this consumer is supplied from; its position in the network
    /// hierarchy (zone to feeder) is derived from it. Null until the consumer is mapped.</summary>
    public Guid? DtrId { get; private set; }

    public Dtr? Dtr { get; private set; }

    /// <summary>
    /// Facts the daily bill needs for an HT/EHT consumer's transformer and CT-PT set, and whether they are metered on the
    /// LT side of their transformer (tariff book §4, §5, Supply Code 2.3.1). All null/false for an LT consumer, who owns
    /// none of this equipment. Set together via <see cref="SetBillingFacts"/>, never piecemeal, so they are never left
    /// inconsistent (for example CPMC opted in without a wiring, or without the LT-side metering the book requires for it).
    /// </summary>
    public SupplyVoltage? SupplyVoltage { get; private set; }

    /// <summary>An HT consumer metered on the LT side of their own transformer (Supply Code 2.3.1): adds the tariff
    /// book's 3% LT-side metering surcharge on the energy charge, and is what makes them eligible for CPMC.</summary>
    public bool MeteredOnLtSide { get; private set; }

    /// <summary>Opted for MePDCL to maintain their own transformer (TMC, tariff book §5); zero unless opted in.</summary>
    public bool TransformerMaintenanceOptedIn { get; private set; }

    /// <summary>The transformer's installed capacity, billed exclusively to its owner (§5.1) — the basis TMC is charged
    /// on. Required when <see cref="TransformerMaintenanceOptedIn"/> is true.</summary>
    public decimal? TransformerCapacityKva { get; private set; }

    /// <summary>Opted for MePDCL to maintain their own CT-PT metering set (CPMC, tariff book §4); zero unless opted in.
    /// Per the book, only eligible when <see cref="MeteredOnLtSide"/> is true.</summary>
    public bool CtPtMaintenanceOptedIn { get; private set; }

    public CtPtWiring? CtPtWiring { get; private set; }

    public Consumer(Guid id, string accountNumber, string name, string serviceAddress, SmartMeter meter, decimal connectedLoadKw, bool isNetMeter = false)
    {
        if (string.IsNullOrWhiteSpace(accountNumber))
            throw new ArgumentException("Account number is required.", nameof(accountNumber));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        if (connectedLoadKw <= 0)
            throw new ArgumentOutOfRangeException(nameof(connectedLoadKw), "Connected load must be positive.");

        Id = id;
        AccountNumber = accountNumber;
        Name = name;
        ServiceAddress = serviceAddress;
        Meter = meter ?? throw new ArgumentNullException(nameof(meter));
        ConnectedLoadKw = connectedLoadKw;
        ConnectionStatus = ConnectionStatus.Active;
        BillingMode = BillingMode.Prepaid;
        IsNetMeter = isNetMeter;
        Wallet = new PrepaidWallet(Guid.NewGuid(), Id);
    }

    // EF Core / serialization
    private Consumer()
    {
        AccountNumber = string.Empty;
        Name = string.Empty;
        ServiceAddress = string.Empty;
        Meter = null!;
        Wallet = null!;
    }

    /// <summary>
    /// True once the wallet has exhausted both its balance and its emergency credit allowance
    /// — the credit-based precondition for disconnection. Other factors (grace period,
    /// exemptions, pending commands, etc.) belong to a separate disconnect decision engine and
    /// are intentionally not part of this simple check.
    /// </summary>
    public bool IsDisconnectEligibleOnCredit => !Wallet.IsWithinEmergencyCredit;

    public void Disconnect()
    {
        if (ConnectionStatus == ConnectionStatus.Disconnected)
            return;

        ConnectionStatus = ConnectionStatus.Disconnected;
    }

    public void Reconnect()
    {
        if (Wallet.Balance <= 0)
            throw new InvalidOperationException("Cannot reconnect a consumer with a zero or negative wallet balance.");

        ConnectionStatus = ConnectionStatus.Active;
    }

    public void RequestDisconnection() => ConnectionStatus = ConnectionStatus.DisconnectionPending;

    public void RequestReconnection() => ConnectionStatus = ConnectionStatus.ReconnectionPending;

    /// <summary>Assigns (or reassigns) the tariff automated LS/DLP billing uses for this
    /// consumer. A domain method rather than a raw setter, per this project's convention that a
    /// meaningful state change is always an explicit, named action.</summary>
    public void AssignTariff(Guid tariffId) => TariffId = tariffId;

    public void AssignDtr(Guid dtrId) => DtrId = dtrId;

    /// <summary>
    /// Records this consumer's transformer/CT-PT/metering-side facts for the daily bill to use (LT-side metering
    /// surcharge, TMC, CPMC — tariff book §4, §5, Supply Code 2.3.1). Set together, not piecemeal, so the combination
    /// is always one the book allows: CPMC needs LT-side metering; either maintenance charge needs a supply voltage;
    /// TMC needs the transformer's installed capacity; CPMC needs its wiring. An LT consumer (no <paramref name="supplyVoltage"/>)
    /// cannot opt into either — this equipment and the LT-side surcharge are HT/EHT-only.
    /// </summary>
    public void SetBillingFacts(
        SupplyVoltage? supplyVoltage, bool meteredOnLtSide,
        bool transformerMaintenanceOptedIn, decimal? transformerCapacityKva,
        bool ctPtMaintenanceOptedIn, CtPtWiring? ctPtWiring)
    {
        if (supplyVoltage is null && (meteredOnLtSide || transformerMaintenanceOptedIn || ctPtMaintenanceOptedIn))
            throw new ArgumentException("A supply voltage is required to record LT-side metering or opt into TMC/CPMC — an LT consumer has neither.", nameof(supplyVoltage));
        if (transformerMaintenanceOptedIn && transformerCapacityKva is not (> 0))
            throw new ArgumentOutOfRangeException(nameof(transformerCapacityKva), "The transformer's installed capacity is required, and must be positive, to opt into TMC.");
        if (ctPtMaintenanceOptedIn && !meteredOnLtSide)
            throw new ArgumentException("CPMC applies only when the consumer is metered on the LT side of their transformer (tariff book §4.2).", nameof(ctPtMaintenanceOptedIn));
        if (ctPtMaintenanceOptedIn && ctPtWiring is null)
            throw new ArgumentException("The CT-PT set's wiring is required to opt into CPMC.", nameof(ctPtWiring));

        SupplyVoltage = supplyVoltage;
        MeteredOnLtSide = meteredOnLtSide;
        TransformerMaintenanceOptedIn = transformerMaintenanceOptedIn;
        TransformerCapacityKva = transformerMaintenanceOptedIn ? transformerCapacityKva : null;
        CtPtMaintenanceOptedIn = ctPtMaintenanceOptedIn;
        CtPtWiring = ctPtMaintenanceOptedIn ? ctPtWiring : null;
    }

    /// <summary>Sets the notification destination.</summary>
    public void SetMobileNumber(string mobileNumber)
    {
        if (!PrepaidEngine.Domain.MobileNumber.TryNormalize(mobileNumber, out var normalized))
            throw new ArgumentException("Enter a valid 10-digit Indian mobile number (starting 6-9), with or without +91.", nameof(mobileNumber));

        MobileNumber = normalized;
    }

    /// <summary>Swaps the consumer's physical meter (see <see cref="MeterAssignment"/> for the
    /// audit trail this must be paired with). Never compares the old and new meter's cumulative
    /// readings against each other — that boundary is exactly what <see cref="MeterAssignment"/>
    /// exists to make explicit, since the two readings belong to different physical meters.</summary>
    public void ReplaceMeter(SmartMeter newMeter)
    {
        Meter = newMeter ?? throw new ArgumentNullException(nameof(newMeter));
    }

    /// <summary>Applies a postpaid-to-prepaid conversion. Only ever called after the
    /// corresponding <see cref="ConversionRequest"/> reaches <see cref="Enums.ConversionStatus.Approved"/>
    /// — never a raw property set — so the decision trail and the actual state change stay two
    /// explicit, auditable steps.</summary>
    public void ConvertToPrepaid()
    {
        if (BillingMode == BillingMode.Prepaid)
            throw new InvalidOperationException("Consumer is already billed Prepaid.");

        BillingMode = BillingMode.Prepaid;
    }

    /// <summary>Applies a prepaid-to-postpaid conversion. See <see cref="ConvertToPrepaid"/> for
    /// why this is a dedicated method rather than a property setter.</summary>
    public void ConvertToPostpaid()
    {
        if (BillingMode == BillingMode.Postpaid)
            throw new InvalidOperationException("Consumer is already billed Postpaid.");

        BillingMode = BillingMode.Postpaid;
    }
}
