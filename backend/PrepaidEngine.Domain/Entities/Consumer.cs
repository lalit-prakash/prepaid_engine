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

    /// <summary>Sets the notification destination.</summary>
    public void SetMobileNumber(string mobileNumber)
    {
        if (string.IsNullOrWhiteSpace(mobileNumber))
            throw new ArgumentException("A mobile number is required.", nameof(mobileNumber));

        MobileNumber = mobileNumber;
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
