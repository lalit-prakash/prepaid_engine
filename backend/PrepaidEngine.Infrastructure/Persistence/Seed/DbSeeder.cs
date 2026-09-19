using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;

namespace PrepaidEngine.Infrastructure.Persistence.Seed;

/// <summary>
/// Inserts a small, realistic set of demo data (one consumer end-to-end through
/// tariff/consumption/billing/recharge) so a fresh database isn't empty for local
/// development or a demo. Sourced from the real MePDCL Domestic (DLT) tariff schedule
/// (see docs/assumptions-and-security.md) rather than made-up numbers.
///
/// Idempotent: does nothing if any consumer already exists.
/// </summary>
public static class DbSeeder
{
    public static async Task SeedAsync(PrepaidEngineDbContext context, CancellationToken cancellationToken = default)
    {
        if (await context.Consumers.AnyAsync(cancellationToken))
            return;

        var tariff = new Tariff(
            Guid.NewGuid(),
            "MePDCL Domestic (DLT)",
            ConsumerCategory.Domestic,
            new[]
            {
                new TariffSlab(0, 100, 5.00m),
                new TariffSlab(100, 200, 5.04m),
                new TariffSlab(200, null, 5.10m)
            },
            fixedChargePerUnitPerMonth: 90.00m,
            prepaidEnergyRebatePercent: 2.00m,
            emergencyCreditLimit: 200.00m,
            minVendAmountSinglePhase: 500.00m,
            maxVendAmountSinglePhase: 15000.00m,
            minVendAmountThreePhase: 500.00m,
            maxVendAmountThreePhase: 25000.00m);

        var meter = new SmartMeter(Guid.NewGuid(), "MTR-DEMO-0001", MeterPhase.SinglePhase);

        var consumer = new Consumer(
            Guid.NewGuid(),
            accountNumber: "DEMO-0001",
            name: "Demo Consumer",
            serviceAddress: "1 Lum Jingshai, Shillong",
            meter: meter,
            connectedLoadKw: 2m);

        // Initial recharge: consumer tops up ₹500 via RMS before any consumption is billed.
        var initialRecharge = new RechargeTransaction(
            Guid.NewGuid(), consumer.Id, 500.00m, "RMS-DEMO-0001", DateTime.UtcNow.AddDays(-10));
        initialRecharge.MarkSuccessful(DateTime.UtcNow.AddDays(-10));
        consumer.Wallet.SetEmergencyCreditLimit(tariff.EmergencyCreditLimit);
        consumer.Wallet.Credit(initialRecharge.Amount, WalletTransactionType.Recharge, initialRecharge.RmsReferenceId);

        // A month's consumption, billed against the tariff and settled from the wallet.
        var reading = new ConsumptionReading(
            Guid.NewGuid(), consumer.Id, consumptionKwh: 45m,
            periodStart: DateTime.UtcNow.AddDays(-10), periodEnd: DateTime.UtcNow);
        meter.RecordReading(cumulativeKwh: 45m, readAt: reading.PeriodEnd);

        var energyChargeGross = tariff.CalculateEnergyCharge(reading.ConsumptionKwh);
        var rebate = energyChargeGross * (tariff.PrepaidEnergyRebatePercent / 100m);
        var fixedCharge = tariff.CalculateFixedCharge(consumer.ConnectedLoadKw);
        var duty = ElectricityDuty.Calculate(tariff.Category, reading.ConsumptionKwh);

        // A small, illustrative FPPAS: a +2% rate notified against a prior month's energy
        // charge (approximated here by this same month's gross EC for demo purposes),
        // deferred and prorated across a 30-day billing month — this bill takes one day's
        // share. See FppasCharge for the real mechanism, sourced from MePDCL's reference workbook.
        var fppasCharge = new FppasCharge(Guid.NewGuid(), sourceEnergyCharge: energyChargeGross, rateFraction: 0.02m, notifiedAt: DateTime.UtcNow.AddDays(-11));
        var fppasDailyShare = fppasCharge.AllocateAcrossDaysRoundedToCents(daysInBillingMonth: 30)[0];

        var bill = new PrepaidBill(
            Guid.NewGuid(), consumer.Id, reading.Id, tariff.Id,
            energyChargeGross: energyChargeGross,
            prepaidRebateAmount: rebate,
            fixedCharge: fixedCharge,
            electricityDutyAmount: duty,
            generatedAt: DateTime.UtcNow,
            fppasAmount: fppasDailyShare,
            fppasChargeId: fppasCharge.Id);
        var billDebit = consumer.Wallet.Debit(bill.Amount, WalletTransactionType.BillDebit, bill.Id.ToString());
        bill.ApplyPayment(-billDebit.Amount); // billDebit.Amount is negative; ApplyPayment expects a positive amount

        context.Meters.Add(meter);
        context.Tariffs.Add(tariff);
        context.Consumers.Add(consumer);
        context.RechargeTransactions.Add(initialRecharge);
        context.ConsumptionReadings.Add(reading);
        context.FppasCharges.Add(fppasCharge);
        context.Bills.Add(bill);

        await context.SaveChangesAsync(cancellationToken);
    }

    private static readonly (string Account, string Name, string Address, MeterPhase Phase, decimal LoadKw, decimal Recharge, decimal Kwh)[] ExtraConsumers =
    {
        ("DEMO-0002", "Ribha Marbaniang", "12 Laitumkhrah, Shillong", MeterPhase.SinglePhase, 3m, 1000m, 120m),
        ("DEMO-0003", "Daniel Syiem", "45 Nongthymmai, Shillong", MeterPhase.SinglePhase, 2m, 600m, 60m),
        ("DEMO-0004", "Bandari Lyngdoh", "8 Mawlai Mawroh, Shillong", MeterPhase.ThreePhase, 6m, 3000m, 310m),
        ("DEMO-0005", "Ioanis Khongwir", "23 Rilbong, Shillong", MeterPhase.SinglePhase, 2m, 500m, 90m),
        ("DEMO-0006", "Phida Nongrum", "5 Umpling, Shillong", MeterPhase.SinglePhase, 4m, 2000m, 180m),
        ("DEMO-0007", "Wanshan Kharkongor", "17 Jaiaw, Shillong", MeterPhase.ThreePhase, 8m, 5000m, 420m),
    };

    /// <summary>
    /// Adds six more demo consumers (DEMO-0002..0007) on top of the original DEMO-0001, each with
    /// a meter, a successful recharge, a billed reading and a wallet balance. Idempotent per
    /// account number; does nothing if no Active Domestic tariff exists yet.
    /// </summary>
    public static async Task SeedExtraConsumersAsync(PrepaidEngineDbContext context, CancellationToken cancellationToken = default)
    {
        var tariff = await context.Tariffs
            .FirstOrDefaultAsync(t => t.Status == TariffLifecycleStatus.Active && t.Category == ConsumerCategory.Domestic, cancellationToken);
        if (tariff is null)
            return;

        var existing = await context.Consumers.Select(c => c.AccountNumber).ToListAsync(cancellationToken);
        var now = DateTime.UtcNow;

        foreach (var (account, name, address, phase, loadKw, rechargeAmount, kwh) in ExtraConsumers)
        {
            if (existing.Contains(account))
                continue;

            var meter = new SmartMeter(Guid.NewGuid(), account.Replace("DEMO", "MTR-DEMO"), phase);
            var consumer = new Consumer(Guid.NewGuid(), account, name, address, meter, loadKw);

            var recharge = new RechargeTransaction(Guid.NewGuid(), consumer.Id, rechargeAmount, $"RMS-{account}", now.AddDays(-10));
            recharge.MarkSuccessful(now.AddDays(-10));
            consumer.Wallet.SetEmergencyCreditLimit(tariff.EmergencyCreditLimit);
            consumer.Wallet.Credit(recharge.Amount, WalletTransactionType.Recharge, recharge.RmsReferenceId);

            var reading = new ConsumptionReading(Guid.NewGuid(), consumer.Id, kwh, now.AddDays(-10), now);
            meter.RecordReading(kwh, reading.PeriodEnd);

            var energy = tariff.CalculateEnergyCharge(kwh);
            var bill = new PrepaidBill(
                Guid.NewGuid(), consumer.Id, reading.Id, tariff.Id,
                energyChargeGross: energy,
                prepaidRebateAmount: energy * (tariff.PrepaidEnergyRebatePercent / 100m),
                fixedCharge: tariff.CalculateFixedCharge(loadKw),
                electricityDutyAmount: ElectricityDuty.Calculate(tariff.Category, kwh),
                generatedAt: now);
            var debit = consumer.Wallet.Debit(bill.Amount, WalletTransactionType.BillDebit, bill.Id.ToString());
            bill.ApplyPayment(-debit.Amount);

            context.Meters.Add(meter);
            context.Consumers.Add(consumer);
            context.RechargeTransactions.Add(recharge);
            context.ConsumptionReadings.Add(reading);
            context.Bills.Add(bill);
        }

        await context.SaveChangesAsync(cancellationToken);
    }
}
