using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Auth;
using PrepaidEngine.Api.Security;
using PrepaidEngine.Api.Dashboard;
using PrepaidEngine.Api.Reports;
using PrepaidEngine.Application.Billing;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Application.Conversion;
using PrepaidEngine.Application.MeterCommands;
using PrepaidEngine.Application.MeterData;
using PrepaidEngine.Application.Rms;
using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Billing;
using PrepaidEngine.Infrastructure.Connectivity;
using PrepaidEngine.Infrastructure.Conversion;
using PrepaidEngine.Infrastructure.MeterCommands;
using PrepaidEngine.Infrastructure.MeterData;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Sla;
using PrepaidEngine.Infrastructure.Tariffs;
using PrepaidEngine.Application.Sla;
using PrepaidEngine.Infrastructure.Persistence.Seed;
using PrepaidEngine.Infrastructure.Rms;
using static PrepaidEngine.Api.ApiHelpers;

namespace PrepaidEngine.Api.Endpoints;

/// <summary>Conversion endpoints, moved out of Program.cs unchanged.</summary>
public static class ConversionEndpoints
{
    public static void MapConversionEndpoints(this WebApplication app)
    {
        // --- Prepaid conversion: RMS pushes a batch of postpaid->prepaid conversion requests. ----------
        // Each item is validated and, if accepted, dispatches a PaymentModeChangeCommand down the
        // MDMS -> HES -> Meter chain (mocked — see IPaymentModeChangeClient); only once that command is
        // Acknowledged does this endpoint complete the ConversionRequest, flip the consumer to Prepaid
        // (Consumer.ConvertToPrepaid(), never a raw property set), credit any FOA/DIA amount, post the
        // conversion's opening charge (1st-of-month reading -> the meter's reading at conversion), queue
        // the "you are now prepaid" SMS, and run the emergency-credit guard once for the newly-created
        // wallet. A NET-meter consumer is always rejected ("If the consumer is NET meter consumer, then
        // such meter shall not be converted to prepaid"). One bad item in the batch does not fail the
        // rest — each is independently validated and reported.
        app.MapPost("/api/v1/conversions", async (
            List<ConversionRequestItem>? requests,
            PrepaidEngineDbContext db,
            IPaymentModeChangeClient paymentModeChangeClient,
            IEmergencyCreditGuard emergencyCreditGuard) =>
        {
            if (requests is null || requests.Count == 0)
                return Results.BadRequest(new { error = "At least one conversion request is required." });

            var responses = new List<ConversionResponseItem>();

            foreach (var item in requests)
            {
                if (string.IsNullOrWhiteSpace(item.TransactionId) || string.IsNullOrWhiteSpace(item.ConsumerNumber))
                {
                    responses.Add(new ConversionResponseItem(item.TransactionId ?? string.Empty, item.ConsumerNumber ?? string.Empty,
                        "Fail", "Transaction ID and Consumer Number are required.", null));
                    continue;
                }

                var consumer = await db.Consumers.Include(c => c.Wallet).ThenInclude(w => w.Transactions)
                    .FirstOrDefaultAsync(c => c.AccountNumber == item.ConsumerNumber);
                if (consumer is null)
                {
                    responses.Add(new ConversionResponseItem(item.TransactionId, item.ConsumerNumber,
                        "Fail", $"No consumer found for consumer number {item.ConsumerNumber}.", null));
                    continue;
                }

                ConversionRequest conversion;
                try
                {
                    conversion = new ConversionRequest(
                        Guid.NewGuid(), consumer.Id, item.TransactionId, item.MeterSerialNumber, item.ConsumerNumber,
                        item.ConsumerType, item.InitialReading, item.InitialReadingDateTime, item.ConversionDate,
                        DateTime.UtcNow, item.LastReadingDate, item.LastBillingDate, item.LastBillFrKwh, item.LastBillFrKvah,
                        item.LastBillMaxDemandKw, item.OutstandingAmount, item.MeterStatus, item.IsPermanentConsumer,
                        item.FoaAmount, item.DiaAmount, item.TemporaryDisconnectionDate, item.ReconnectionDate,
                        item.RequestType ?? "PRE");
                }
                catch (ArgumentException ex)
                {
                    responses.Add(new ConversionResponseItem(item.TransactionId, item.ConsumerNumber, "Fail", ex.Message, null));
                    continue;
                }

                db.ConversionRequests.Add(conversion);

                if (consumer.IsNetMeter)
                {
                    conversion.Reject("NET meter consumers cannot be converted to prepaid.", DateTime.UtcNow);
                    Audit(db, nameof(ConversionRequest), conversion.Id.ToString(), "Rejected", "system", details: conversion.DecisionNote);
                    responses.Add(new ConversionResponseItem(item.TransactionId, item.ConsumerNumber, "Fail", conversion.DecisionNote!, null));
                    continue;
                }

                if (consumer.BillingMode == BillingMode.Prepaid)
                {
                    conversion.Reject("Consumer is already billed Prepaid.", DateTime.UtcNow);
                    Audit(db, nameof(ConversionRequest), conversion.Id.ToString(), "Rejected", "system", details: conversion.DecisionNote);
                    responses.Add(new ConversionResponseItem(item.TransactionId, item.ConsumerNumber, "Fail", conversion.DecisionNote!, null));
                    continue;
                }

                // Dispatch the payment-mode-change command: MDMS -> HES -> Meter -> HES -> MDMS. The
                // request stays Requested (not yet Approved) until the command actually acknowledges —
                // Reject() only accepts a Requested request, so approving upfront would make a
                // Failed/TimedOut outcome below throw instead of cleanly rejecting the request.
                var pmcCommand = new PaymentModeChangeCommand(Guid.NewGuid(), conversion.Id, consumer.Id, DateTime.UtcNow);
                db.PaymentModeChangeCommands.Add(pmcCommand);
                pmcCommand.MarkSent(DateTime.UtcNow);

                var pmcResult = await paymentModeChangeClient.ChangePaymentModeAsync(new PaymentModeChangeRequest(
                    consumer.Id, item.MeterSerialNumber, item.TransactionId, item.InitialReading, item.InitialReadingDateTime, item.ConversionDate));

                if (pmcResult.Outcome != PaymentModeChangeOutcome.Acknowledged)
                {
                    if (pmcResult.Outcome == PaymentModeChangeOutcome.Failed)
                        pmcCommand.MarkFailed(pmcResult.Message ?? "Meter/HES rejected the payment-mode-change command.");
                    else
                        pmcCommand.MarkTimedOut();

                    RaiseException(db, OperationalExceptionSourceType.ConnectivityCommand, pmcCommand.Id, consumer.Id,
                        $"Payment-mode-change command {pmcCommand.Id} for conversion {conversion.Id} did not acknowledge: {pmcResult.Message ?? pmcCommand.Status.ToString()}.");

                    conversion.Reject($"Payment-mode-change command {pmcCommand.Status}: {pmcResult.Message ?? "no detail"}.", DateTime.UtcNow);
                    Audit(db, nameof(ConversionRequest), conversion.Id.ToString(), "Rejected", "system", details: conversion.DecisionNote);
                    responses.Add(new ConversionResponseItem(item.TransactionId, item.ConsumerNumber, "Fail", conversion.DecisionNote!, pmcCommand.Status.ToString()));
                    continue;
                }

                pmcCommand.MarkAcknowledged(DateTime.UtcNow, pmcResult.MeterReadingAtConversion!.Value);
                conversion.Approve(DateTime.UtcNow);
                conversion.RecordReadingAtConversion(pmcResult.MeterReadingAtConversion.Value);
                conversion.Complete(DateTime.UtcNow);

                var oldMode = consumer.BillingMode.ToString();
                consumer.ConvertToPrepaid();

                Audit(db, nameof(Consumer), consumer.Id.ToString(), "BillingModeChanged", "system",
                    oldValue: oldMode, newValue: consumer.BillingMode.ToString(), details: $"Conversion request {conversion.Id} completed");

                // FOA/DIA: credited into the new prepaid wallet as-is (RMS itself zeroes both above the
                // Rs. 10,000 outstanding threshold — enforced as a validation guard in ConversionRequest's
                // constructor, not computed here).
                var foaDiaTotal = conversion.FoaAmount + conversion.DiaAmount;
                if (foaDiaTotal > 0)
                {
                    var foaDiaCredit = consumer.Wallet.Credit(foaDiaTotal, WalletTransactionType.ConversionFoaDiaCredit, $"CONV-FOADIA:{conversion.Id}");
                    db.WalletTransactions.Add(foaDiaCredit);
                }

                // Opening bill: consumption from the 1st of the conversion month up to the moment of
                // conversion (InitialReading -> ReadingAtConversion), billed at this consumer's tariff —
                // ongoing prepaid billing (via DLP) starts from the day after the conversion date.
                var openingConsumption = conversion.OpeningConsumptionKwh!.Value;
                if (openingConsumption > 0 && consumer.TariffId is not null)
                {
                    var tariff = await db.Tariffs.FirstOrDefaultAsync(t => t.Id == consumer.TariffId);
                    if (tariff is not null)
                    {
                        var grossEnergyCharge = tariff.CalculateEnergyCharge(openingConsumption);
                        var rebate = grossEnergyCharge * (tariff.PrepaidEnergyRebatePercent / 100m);
                        var openingCharge = Math.Round(grossEnergyCharge - rebate, 2, MidpointRounding.AwayFromZero);
                        if (openingCharge > 0)
                        {
                            var openingDebit = consumer.Wallet.Debit(openingCharge, WalletTransactionType.ConversionOpeningCharge, $"CONV-OPEN:{conversion.Id}");
                            db.WalletTransactions.Add(openingDebit);
                        }
                    }
                }

                db.NotificationEvents.Add(new NotificationEvent(
                    Guid.NewGuid(), consumer.Id, NotificationEventType.PrepaidConversionCompleted,
                    $"Dear Consumer, your account {consumer.AccountNumber} has been converted from postpaid to prepaid billing.",
                    DateTime.UtcNow));

                await emergencyCreditGuard.EvaluateAsync(consumer);

                responses.Add(new ConversionResponseItem(item.TransactionId, item.ConsumerNumber, "Success", null, pmcCommand.Status.ToString()));
            }

            await db.SaveChangesAsync();

            return Results.Ok(responses);
        })
        .WithName("SubmitConversions")
        .RequireAuthorization("Operations");


        app.MapGet("/api/v1/conversions", async (HttpContext http, PrepaidEngineDbContext db) =>
        {
            var conversions = await (
                from cv in db.ConversionRequests
                join c in db.Consumers on cv.ConsumerId equals c.Id
                orderby cv.RequestedAt descending
                select new
                {
                    cv.Id,
                    c.AccountNumber,
                    c.Name,
                    cv.TransactionId,
                    cv.MeterSerialNumber,
                    cv.RequestType,
                    cv.ConsumerType,
                    cv.InitialReading,
                    cv.InitialReadingDateTime,
                    cv.ConversionDate,
                    cv.GracePeriodEndDate,
                    cv.Status,
                    cv.DecisionNote,
                    cv.RequestedAt,
                    cv.DecidedAt,
                    cv.CompletedAt,
                    cv.LastReadingDate,
                    cv.LastBillingDate,
                    cv.TemporaryDisconnectionDate,
                    cv.ReconnectionDate,
                    cv.LastBillFrKwh,
                    cv.LastBillFrKvah,
                    cv.LastBillMaxDemandKw,
                    cv.OutstandingAmount,
                    cv.MeterStatus,
                    cv.IsPermanentConsumer,
                    cv.FoaAmount,
                    cv.DiaAmount,
                    cv.ReadingAtConversion,
                })
                .ToCappedListAsync(http);

            return Results.Ok(conversions);
        })
        .WithName("ListConversions")
        .RequireAuthorization();


        app.MapGet("/api/v1/conversions/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
        {
            var conversion = await db.ConversionRequests.FirstOrDefaultAsync(cv => cv.Id == id);
            if (conversion is null)
                return Results.NotFound();

            var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.Id == conversion.ConsumerId);
            if (consumer is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                    title: "Conversion request references missing data",
                    detail: $"Conversion request {id} references a consumer that no longer exists.");
            }

            var paymentModeChange = await db.PaymentModeChangeCommands.FirstOrDefaultAsync(p => p.ConversionRequestId == conversion.Id);

            return Results.Ok(new
            {
                conversion.Id,
                Consumer = new { consumer.AccountNumber, consumer.Name, consumer.BillingMode },
                conversion.TransactionId,
                conversion.MeterSerialNumber,
                conversion.RequestType,
                conversion.ConsumerType,
                conversion.InitialReading,
                conversion.InitialReadingDateTime,
                conversion.ConversionDate,
                conversion.GracePeriodEndDate,
                conversion.Status,
                conversion.DecisionNote,
                conversion.RequestedAt,
                conversion.DecidedAt,
                conversion.CompletedAt,
                conversion.LastReadingDate,
                conversion.LastBillingDate,
                conversion.TemporaryDisconnectionDate,
                conversion.ReconnectionDate,
                conversion.LastBillFrKwh,
                conversion.LastBillFrKvah,
                conversion.LastBillMaxDemandKw,
                conversion.OutstandingAmount,
                conversion.MeterStatus,
                conversion.IsPermanentConsumer,
                conversion.FoaAmount,
                conversion.DiaAmount,
                conversion.ReadingAtConversion,
                conversion.OpeningConsumptionKwh,
                PaymentModeChange = paymentModeChange is null ? null : new
                {
                    paymentModeChange.Id,
                    paymentModeChange.Status,
                    paymentModeChange.ErrorMessage,
                    paymentModeChange.MeterReadingAtConversion,
                    paymentModeChange.CreatedAt,
                    paymentModeChange.SentAt,
                    paymentModeChange.AcknowledgedAt,
                },
            });
        })
        .WithName("GetConversionById")
        .RequireAuthorization();


        // --- Prepaid -> Postpaid (reverse conversion) — never RMS-pushed (see ReverseConversionRequest's
        // doc comment), so this completes in one operator-authorized step rather than waiting on an
        // external decision. Conversion-safety checks (spec section 2.11): reject a consumer already
        // Postpaid (duplicate conversion), one with an open billing hold, or one with a still-pending
        // forward conversion request — each is a real, checkable condition, never assumed.
        app.MapPost("/api/v1/conversions/reverse", async (ReverseConversionApiRequest request, PrepaidEngineDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(request.ConsumerNumber))
                return Results.BadRequest(new { error = "Consumer number is required." });

            var consumer = await db.Consumers.Include(c => c.Meter).Include(c => c.Wallet)
                .FirstOrDefaultAsync(c => c.AccountNumber == request.ConsumerNumber);
            if (consumer is null)
                return Results.NotFound(new { error = $"No consumer found for consumer number {request.ConsumerNumber}." });

            ReverseConversionRequest reverseConversion;
            try
            {
                reverseConversion = new ReverseConversionRequest(Guid.NewGuid(), consumer.Id, request.RequestedBy, request.Reason, DateTime.UtcNow);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }

            db.ReverseConversionRequests.Add(reverseConversion);

            if (consumer.BillingMode == BillingMode.Postpaid)
            {
                reverseConversion.Reject("Consumer is already billed Postpaid.", DateTime.UtcNow);
                Audit(db, nameof(ReverseConversionRequest), reverseConversion.Id.ToString(), "Rejected", request.RequestedBy, details: reverseConversion.DecisionNote);
                await db.SaveChangesAsync();
                return Results.BadRequest(new { error = reverseConversion.DecisionNote });
            }

            var openHold = await db.MeterBillingControls.FirstOrDefaultAsync(m => m.ConsumerId == consumer.Id && m.ActualBillingBlocked);
            if (openHold is not null)
            {
                reverseConversion.Reject($"Consumer has an open billing hold: {openHold.BlockReason}", DateTime.UtcNow);
                Audit(db, nameof(ReverseConversionRequest), reverseConversion.Id.ToString(), "Rejected", request.RequestedBy, details: reverseConversion.DecisionNote);
                await db.SaveChangesAsync();
                return Results.BadRequest(new { error = reverseConversion.DecisionNote });
            }

            var pendingForwardConversion = await db.ConversionRequests.FirstOrDefaultAsync(
                cv => cv.ConsumerId == consumer.Id && (cv.Status == ConversionStatus.Requested || cv.Status == ConversionStatus.Approved));
            if (pendingForwardConversion is not null)
            {
                reverseConversion.Reject("Consumer has a pending postpaid-to-prepaid conversion request awaiting completion.", DateTime.UtcNow);
                Audit(db, nameof(ReverseConversionRequest), reverseConversion.Id.ToString(), "Rejected", request.RequestedBy, details: reverseConversion.DecisionNote);
                await db.SaveChangesAsync();
                return Results.BadRequest(new { error = reverseConversion.DecisionNote });
            }

            var pendingReverseConversion = await db.ReverseConversionRequests.FirstOrDefaultAsync(
                r => r.ConsumerId == consumer.Id && r.Status == ReverseConversionStatus.Requested && r.Id != reverseConversion.Id);
            if (pendingReverseConversion is not null)
            {
                reverseConversion.Reject("Consumer already has a reverse conversion request in progress.", DateTime.UtcNow);
                Audit(db, nameof(ReverseConversionRequest), reverseConversion.Id.ToString(), "Rejected", request.RequestedBy, details: reverseConversion.DecisionNote);
                await db.SaveChangesAsync();
                return Results.BadRequest(new { error = reverseConversion.DecisionNote });
            }

            reverseConversion.Complete(consumer.Meter.LastReadingKwh, consumer.Wallet.Balance, DateTime.UtcNow);
            consumer.ConvertToPostpaid();
            Audit(db, nameof(ReverseConversionRequest), reverseConversion.Id.ToString(), "Completed", request.RequestedBy,
                details: $"Final meter reading {reverseConversion.FinalMeterReadingKwh} kWh, final wallet balance {reverseConversion.FinalWalletBalance:C}.");

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                reverseConversion.Id,
                consumer.AccountNumber,
                consumer.BillingMode,
                reverseConversion.Status,
                reverseConversion.FinalMeterReadingKwh,
                reverseConversion.FinalWalletBalance,
                reverseConversion.CompletedAt,
            });
        })
        .WithName("ConvertToPostpaid")
        .RequireAuthorization("Operations");


        app.MapGet("/api/v1/conversions/reverse", async (HttpContext http, PrepaidEngineDbContext db) =>
        {
            var conversions = await (
                from r in db.ReverseConversionRequests
                join c in db.Consumers on r.ConsumerId equals c.Id
                orderby r.RequestedAt descending
                select new
                {
                    r.Id, c.AccountNumber, c.Name, r.RequestedBy, r.Reason, r.Status, r.DecisionNote,
                    r.FinalMeterReadingKwh, r.FinalWalletBalance, r.RequestedAt, r.CompletedAt,
                })
                .Take(500)
                .ToCappedListAsync(http);

            return Results.Ok(conversions);
        })
        .WithName("ListReverseConversions")
        .RequireAuthorization();


        app.MapGet("/api/v1/reconciliation-adjustments", async (HttpContext http, PrepaidEngineDbContext db) =>
        {
            var adjustments = await (
                from r in db.ReconciliationAdjustments
                join c in db.Consumers on r.ConsumerId equals c.Id
                orderby r.AppliedAt descending
                select new
                {
                    r.Id,
                    c.AccountNumber,
                    c.Name,
                    r.Amount,
                    r.PaymentMode,
                    r.ReconciliationDate,
                    r.Reference,
                    r.BalanceAfter,
                    r.AppliedAt,
                })
                .ToCappedListAsync(http);

            return Results.Ok(adjustments);
        })
        .WithName("ListReconciliationAdjustments")
        .RequireAuthorization();


        app.MapGet("/api/v1/reconciliation-adjustments/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
        {
            var adjustment = await db.ReconciliationAdjustments.FirstOrDefaultAsync(r => r.Id == id);
            if (adjustment is null)
                return Results.NotFound();

            var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.Id == adjustment.ConsumerId);
            if (consumer is null)
            {
                return Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                    title: "Reconciliation adjustment references missing data",
                    detail: $"Reconciliation adjustment {id} references a consumer that no longer exists.");
            }

            return Results.Ok(new
            {
                adjustment.Id,
                Consumer = new { consumer.AccountNumber, consumer.Name },
                adjustment.Amount,
                adjustment.PaymentMode,
                adjustment.ReconciliationDate,
                adjustment.Reference,
                adjustment.BalanceAfter,
                adjustment.AppliedAt,
            });
        })
        .WithName("GetReconciliationAdjustmentById")
        .RequireAuthorization();


        // The daily billing-data export AMISP would push to RMS (spec section 7) so RMS can reconcile it
        // against its own shadow monthly bill. Only fields this domain genuinely tracks are populated —
        // cumulative midnight import/export kWh readings are NOT modeled anywhere in this system
        // (ConsumptionReading only stores a period delta, and there is no export/feed-in tracking at
        // all), so those four fields are always null here rather than fabricated. Charge codes map to
        // the three PrepaidBill components closest to the spec's own examples (Energy Charge, Meter
        // Rent/Fixed Charge, Street Light/Public Lighting) — everything else on the bill (duty, FPPAS,
        // TMC, CPMC, arrears) is real but has no RMS charge-code slot in this 3-code payload, so it is
        // intentionally left off this export rather than mislabeled under one of the three codes.
        app.MapGet("/api/v1/billing-reconciliation/daily-export", async (DateTime date, PrepaidEngineDbContext db) =>
        {
            var dayStart = date.Date;
            var dayEnd = dayStart.AddDays(1);

            var rows = await (
                from b in db.Bills
                join c in db.Consumers on b.ConsumerId equals c.Id
                where b.GeneratedAt >= dayStart && b.GeneratedAt < dayEnd
                select new
                {
                    MeterReadingDate = b.GeneratedAt.Date,
                    BillNumber = b.Id,
                    c.AccountNumber,
                    c.Meter.MeterNumber,
                    CumulativeImportKwhNextDay = (decimal?)null,
                    CumulativeExportKwhNextDay = (decimal?)null,
                    CumulativeImportKwh = (decimal?)null,
                    CumulativeExportKwh = (decimal?)null,
                    ChargeCode1 = "ENERGY",
                    ChargeCode1Amount = b.EnergyChargeNet,
                    ChargeCode2 = "FIXED",
                    ChargeCode2Amount = b.FixedCharge,
                    ChargeCode3 = "PUBLIC_LIGHTING",
                    ChargeCode3Amount = 0m,
                })
                .ToListAsync();

            return Results.Ok(rows);
        })
        .WithName("GetDailyBillingReconciliationExport")
        .RequireAuthorization();
    }
}
