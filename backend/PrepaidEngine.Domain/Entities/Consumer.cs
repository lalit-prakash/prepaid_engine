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
    public SmartMeter Meter { get; private set; }
    public PrepaidWallet Wallet { get; private set; }

    /// <summary>Connected load (kW) or contract demand (kVA), used to compute the tariff's fixed/demand charge.</summary>
    public decimal ConnectedLoadKw { get; private set; }

    public Consumer(Guid id, string accountNumber, string name, string serviceAddress, SmartMeter meter, decimal connectedLoadKw)
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
}
