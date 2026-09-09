using System.Linq;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class PrepaidWalletTests
{
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

    [Fact]
    public void Consumer_CannotReconnect_WhenBalanceNotPositive()
    {
        var meter = new SmartMeter(Guid.NewGuid(), "MTR-001");
        var consumer = new Consumer(Guid.NewGuid(), "ACC-001", "Test Consumer", "123 Main St", meter);
        consumer.Disconnect();

        Assert.Throws<InvalidOperationException>(() => consumer.Reconnect());
    }

    [Fact]
    public void Consumer_CanReconnect_WhenWalletHasPositiveBalance()
    {
        var meter = new SmartMeter(Guid.NewGuid(), "MTR-002");
        var consumer = new Consumer(Guid.NewGuid(), "ACC-002", "Test Consumer", "123 Main St", meter);
        consumer.Disconnect();
        consumer.Wallet.Credit(100m, WalletTransactionType.Recharge);

        consumer.Reconnect();

        Assert.Equal(ConnectionStatus.Active, consumer.ConnectionStatus);
    }
}
