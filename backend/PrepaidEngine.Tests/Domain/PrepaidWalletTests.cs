using System.Linq;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class PrepaidWalletTests
{
    private static Consumer BuildConsumer(string accountNumber = "ACC-001", string meterNumber = "MTR-001") =>
        new(Guid.NewGuid(), accountNumber, "Test Consumer", "123 Main St",
            new SmartMeter(Guid.NewGuid(), meterNumber, MeterPhase.SinglePhase),
            connectedLoadKw: 2m);

    [Fact]
    public void Credit_IncreasesBalanceAndRecordsTransaction()
    {
        var wallet = new PrepaidWallet(Guid.NewGuid(), Guid.NewGuid());

        wallet.Credit(500m, WalletTransactionType.Recharge, "RMS-REF-1");

        Assert.Equal(500m, wallet.Balance);
        Assert.Single(wallet.Transactions);
        Assert.Equal(500m, wallet.Transactions.Single().Amount);
    }

    [Fact]
    public void Debit_DecreasesBalanceAndCanGoNegative()
    {
        var wallet = new PrepaidWallet(Guid.NewGuid(), Guid.NewGuid());
        wallet.Credit(100m, WalletTransactionType.Recharge);

        wallet.Debit(150m, WalletTransactionType.BillDebit, "BILL-1");

        Assert.Equal(-50m, wallet.Balance);
    }

    [Fact]
    public void Credit_NonPositiveAmount_Throws()
    {
        var wallet = new PrepaidWallet(Guid.NewGuid(), Guid.NewGuid());

        Assert.Throws<ArgumentOutOfRangeException>(() => wallet.Credit(0m, WalletTransactionType.Recharge));
    }

    [Theory]
    [InlineData(0, 200, true)]      // exactly zero balance: still within emergency credit
    [InlineData(-200, 200, true)]   // exactly at the emergency credit limit
    [InlineData(-200.01, 200, false)] // just beyond it
    [InlineData(50, 200, true)]     // positive balance
    public void IsWithinEmergencyCredit_ReflectsBalanceAgainstLimit(decimal balanceAfterDebit, decimal limit, bool expected)
    {
        var wallet = new PrepaidWallet(Guid.NewGuid(), Guid.NewGuid());
        wallet.SetEmergencyCreditLimit(limit);
        if (balanceAfterDebit < 0)
            wallet.Debit(-balanceAfterDebit, WalletTransactionType.BillDebit);
        else if (balanceAfterDebit > 0)
            wallet.Credit(balanceAfterDebit, WalletTransactionType.Recharge);

        Assert.Equal(expected, wallet.IsWithinEmergencyCredit);
    }

    [Fact]
    public void EmergencyCreditUsed_CappedAtLimitEvenIfBalanceMoreNegative()
    {
        var wallet = new PrepaidWallet(Guid.NewGuid(), Guid.NewGuid());
        wallet.SetEmergencyCreditLimit(200m);
        wallet.Debit(500m, WalletTransactionType.BillDebit);

        Assert.Equal(200m, wallet.EmergencyCreditUsed);
        Assert.False(wallet.IsWithinEmergencyCredit);
    }

    [Fact]
    public void Consumer_CannotReconnect_WhenBalanceNotPositive()
    {
        var consumer = BuildConsumer();
        consumer.Disconnect();

        Assert.Throws<InvalidOperationException>(() => consumer.Reconnect());
    }

    [Fact]
    public void Consumer_CanReconnect_WhenWalletHasPositiveBalance()
    {
        var consumer = BuildConsumer("ACC-002", "MTR-002");
        consumer.Disconnect();
        consumer.Wallet.Credit(100m, WalletTransactionType.Recharge);

        consumer.Reconnect();

        Assert.Equal(ConnectionStatus.Active, consumer.ConnectionStatus);
    }

    [Fact]
    public void Consumer_IsDisconnectEligibleOnCredit_FalseWhileWithinEmergencyCredit()
    {
        var consumer = BuildConsumer("ACC-003", "MTR-003");
        consumer.Wallet.SetEmergencyCreditLimit(200m);
        consumer.Wallet.Debit(150m, WalletTransactionType.BillDebit);

        Assert.False(consumer.IsDisconnectEligibleOnCredit);
    }

    [Fact]
    public void Consumer_IsDisconnectEligibleOnCredit_TrueOnceEmergencyCreditExhausted()
    {
        var consumer = BuildConsumer("ACC-004", "MTR-004");
        consumer.Wallet.SetEmergencyCreditLimit(200m);
        consumer.Wallet.Debit(250m, WalletTransactionType.BillDebit);

        Assert.True(consumer.IsDisconnectEligibleOnCredit);
    }
}
