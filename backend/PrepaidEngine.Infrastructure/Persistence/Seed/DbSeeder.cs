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
}
