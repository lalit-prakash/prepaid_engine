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

/// <summary>Consumer endpoints, moved out of Program.cs unchanged.</summary>
public static class ConsumerEndpoints
{
    public static void MapConsumerEndpoints(this WebApplication app)
    {
        // Demo/local-only read endpoints, behind HTTP Basic auth (DemoAuth:Username/Password, set
        // via user-secrets). This is a stop-gap for a local demo, not a substitute for real
        // authentication/authorization before any shared or production exposure — see
        // docs/assumptions-and-security.md.
        app.MapGet("/api/v1/consumers", async (HttpContext http, PrepaidEngineDbContext db) =>
        {
            var consumers = await db.Consumers
                .Include(c => c.Meter)
                .Include(c => c.Wallet)
                .Select(c => new
                {
                    c.AccountNumber,
                    c.Name,
                    c.ConnectionStatus,
                    MeterNumber = c.Meter.MeterNumber,
                    WalletBalance = c.Wallet.Balance,
                    c.Wallet.EmergencyCreditLimit
                })
                .ToCappedListAsync(http);

            return Results.Ok(consumers);
        })
        .WithName("ListConsumers")
        .RequireAuthorization();


        // Server-side searchable, keyset-paginated consumer list for the Consumers screen (the
        // unpaginated ListConsumers above stays for the dashboard's aggregates). Identifier fields match
        // by prefix so they can use indexes; name matches by substring. Keyset on AccountNumber (unique)
        // avoids deep OFFSET. "Low balance" here means balance below the emergency-credit limit, the
        // same definition the dashboard uses; a configurable threshold does not exist yet.
        app.MapGet("/api/v1/consumers/search", async (
            string? q, PrepaidEngine.Domain.Enums.ConnectionStatus? status, bool? lowBalance,
            string? after, int? pageSize, PrepaidEngineDbContext db,
            Microsoft.Extensions.Options.IOptions<PrepaidEngine.Application.Wallets.LowBalanceOptions> lowBalanceOptions) =>
        {
            var size = Math.Clamp(pageSize ?? 25, 1, 100);

            decimal? lowBalanceThreshold = lowBalanceOptions.Value.ThresholdRs; // null: below each wallet's own emergency credit limit
            var query = db.Consumers.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(q))
            {
                var term = q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
                var prefix = term + "%";
                var contains = "%" + term + "%";
                query = query.Where(c =>
                    EF.Functions.ILike(c.AccountNumber, prefix, "\\") ||
                    EF.Functions.ILike(c.Meter.MeterNumber, prefix, "\\") ||
                    (c.MobileNumber != null && EF.Functions.ILike(c.MobileNumber, prefix, "\\")) ||
                    EF.Functions.ILike(c.Name, contains, "\\"));
            }
            if (status.HasValue)
                query = query.Where(c => c.ConnectionStatus == status.Value);
            if (lowBalance == true)
                query = query.Where(c => c.Wallet.Balance < (lowBalanceThreshold ?? c.Wallet.EmergencyCreditLimit));

            var totalCount = await query.CountAsync();

            if (!string.IsNullOrEmpty(after))
                query = query.Where(c => string.Compare(c.AccountNumber, after) > 0);

            var rows = await query
                .OrderBy(c => c.AccountNumber)
                .Take(size + 1)
                .Select(c => new
                {
                    c.AccountNumber,
                    c.Name,
                    c.MobileNumber,
                    c.ConnectionStatus,
                    MeterNumber = c.Meter.MeterNumber,
                    WalletBalance = c.Wallet.Balance,
                    c.Wallet.EmergencyCreditLimit,
                    LowBalance = c.Wallet.Balance < (lowBalanceThreshold ?? c.Wallet.EmergencyCreditLimit),
                    LastRechargeAt = db.RechargeTransactions
                        .Where(r => r.ConsumerId == c.Id && r.Status == PrepaidEngine.Domain.Enums.RechargeStatus.Success)
                        .Max(r => r.CompletedAt),
                })
                .ToListAsync();

            var hasMore = rows.Count > size;
            var items = hasMore ? rows.Take(size).ToList() : rows;
            return Results.Ok(new { items, nextCursor = hasMore ? items[^1].AccountNumber : null, totalCount });
        })
        .WithName("SearchConsumers")
        .RequireAuthorization();


        app.MapGet("/api/v1/consumers/{accountNumber}", async (string accountNumber, PrepaidEngineDbContext db, Microsoft.Extensions.Options.IOptions<PrepaidEngine.Application.Wallets.LowBalanceOptions> lowBalanceOptions) =>
        {
            var consumer = await db.Consumers
                .Include(c => c.Meter)
                .Include(c => c.Wallet).ThenInclude(w => w.Transactions)
                .FirstOrDefaultAsync(c => c.AccountNumber == accountNumber);

            if (consumer is null)
                return Results.NotFound();

            var bills = await db.Bills
                .Where(b => b.ConsumerId == consumer.Id)
                .Select(b => new
                {
                    b.Id,
                    b.EnergyChargeGross,
                    b.PrepaidRebateAmount,
                    EnergyChargeNet = b.EnergyChargeGross - b.PrepaidRebateAmount,
                    b.FixedCharge,
                    b.ElectricityDutyAmount,
                    b.FppasAmount,
                    b.FppasChargeId,
                    b.TmcAmount,
                    b.CpmcAmount,
                    b.ArrearsAmount,
                    b.ArrearsRecovered,
                    b.Amount,
                    b.AmountPaid,
                    b.Status,
                    b.GeneratedAt
                })
                .ToListAsync();

            // Post-conversion grace period (spec section 1): a -300 threshold applies instead of the
            // normal -200 emergency-credit line for 5 working days after a completed prepaid conversion.
            // NOTE: this system has no automated credit-based disconnect trigger — disconnection here is
            // always an explicit manual operator action (see the disconnect endpoint) — so this is
            // exposed for operator awareness/reporting rather than gating anything automatically; a real
            // auto-disconnect decision engine is out of this project's current scope.
            var latestCompletedConversion = await db.ConversionRequests
                .Where(cv => cv.ConsumerId == consumer.Id && cv.Status == ConversionStatus.Completed)
                .OrderByDescending(cv => cv.ConversionDate)
                .FirstOrDefaultAsync();
            var isWithinConversionGracePeriod = latestCompletedConversion?.IsWithinGracePeriod(DateTime.UtcNow) ?? false;
            var effectiveDisconnectThreshold = isWithinConversionGracePeriod
                ? ConversionRequest.GracePeriodDisconnectThreshold
                : -consumer.Wallet.EmergencyCreditLimit;

            var network = await db.Consumers.AsNoTracking()
                .Where(c => c.Id == consumer.Id)
                .Select(c => new
                {
                    Zone = c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Zone.Name,
                    Circle = c.Dtr!.Feeder.Substation.SubDivision.Division.Circle.Name,
                    Division = c.Dtr!.Feeder.Substation.SubDivision.Division.Name,
                    SubDivision = c.Dtr!.Feeder.Substation.SubDivision.Name,
                    Substation = c.Dtr!.Feeder.Substation.Name,
                    Feeder = c.Dtr!.Feeder.Name,
                    FeederCode = c.Dtr!.Feeder.Code,
                    Dtr = c.Dtr!.Name,
                    DtrCode = c.Dtr!.Code,
                })
                .FirstAsync();

            return Results.Ok(new
            {
                consumer.Id,
                consumer.AccountNumber,
                consumer.Name,
                consumer.MobileNumber,
                consumer.ServiceAddress,
                Network = network,
                consumer.ConnectionStatus,
                consumer.ConnectedLoadKw,
                consumer.IsDisconnectEligibleOnCredit,
                IsWithinConversionGracePeriod = isWithinConversionGracePeriod,
                EffectiveDisconnectThreshold = effectiveDisconnectThreshold,
                Meter = new { consumer.Meter.MeterNumber, consumer.Meter.Phase, consumer.Meter.LastReadingKwh },
                Wallet = new
                {
                    consumer.Wallet.Balance,
                    consumer.Wallet.EmergencyCreditLimit,
                    consumer.Wallet.IsWithinEmergencyCredit,
                    LowBalance = consumer.Wallet.Balance < (lowBalanceOptions.Value.ThresholdRs ?? consumer.Wallet.EmergencyCreditLimit),
                    Transactions = consumer.Wallet.Transactions.Select(t => new { t.Amount, t.Type, t.OccurredAt, t.Reference })
                },
                Bills = bills
            });
        })
        .WithName("GetConsumerByAccountNumber")
        .RequireAuthorization();


        // RC/DC workflow: disconnect and reconnect, orchestrated through IConnectivityCommandClient
        // (MockConnectivityCommandClient for now — see the TODO above). Consumer.ConnectionStatus
        // records local intent (set to *Pending immediately); the ConnectivityCommand's own lifecycle
        // records whether the meter actually acknowledged the change — the same command/acknowledgement
        // split MeterCommand already applies to meter credit. The consumer's status only advances to
        // its final Disconnected/Active value once the dispatched command reaches Acknowledged.
        app.MapPost("/api/v1/consumers/{accountNumber}/disconnect", async (
            string accountNumber,
            ConnectivityRequest request,
            ClaimsPrincipal user,
            PrepaidEngineDbContext db,
            IConnectivityCommandClient connectivityClient) =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "A reason is required to disconnect a consumer." });

            var consumer = await db.Consumers.Include(c => c.Wallet).FirstOrDefaultAsync(c => c.AccountNumber == accountNumber);
            if (consumer is null)
                return Results.NotFound();

            if (consumer.ConnectionStatus != ConnectionStatus.Active)
            {
                return Results.Conflict(new { error = $"Cannot disconnect a consumer whose connection status is {consumer.ConnectionStatus}." });
            }

            // "Happy Hours" — disconnection may only be dispatched 9:00 AM-2:00 PM (spec section 5).
            if (!IsWithinDisconnectWindow(DateTime.UtcNow))
            {
                return Results.BadRequest(new { error = "Disconnection can only be dispatched between 9:00 AM and 2:00 PM (Happy Hours)." });
            }

            consumer.RequestDisconnection();

            var command = new ConnectivityCommand(Guid.NewGuid(), consumer.Id, ConnectivityCommandType.Disconnect, request.Reason, DateTime.UtcNow);
            db.ConnectivityCommands.Add(command);
            command.MarkSent(DateTime.UtcNow);

            var correlationId = request.CorrelationId ?? $"disconnect-{Guid.NewGuid():N}";
            var result = await connectivityClient.SendConnectivityCommandAsync(
                new SendConnectivityCommandRequest(consumer.Id, ConnectivityCommandType.Disconnect, correlationId));

            switch (result.Outcome)
            {
                case ConnectivityCommandOutcome.Acknowledged:
                    command.MarkAcknowledged(DateTime.UtcNow);
                    consumer.Disconnect();
                    break;
                case ConnectivityCommandOutcome.Failed:
                    command.MarkFailed(result.Message ?? "Meter rejected the disconnect command.");
                    RaiseException(db, OperationalExceptionSourceType.ConnectivityCommand, command.Id, consumer.Id,
                        $"Disconnect command {command.Id} failed: {command.ErrorMessage}");
                    break;
                case ConnectivityCommandOutcome.TimedOut:
                    command.MarkTimedOut();
                    RaiseException(db, OperationalExceptionSourceType.ConnectivityCommand, command.Id, consumer.Id,
                        $"Disconnect command {command.Id} timed out waiting for meter acknowledgement.");
                    break;
            }

            Audit(db, nameof(ConnectivityCommand), command.Id.ToString(), "Dispatched", user.Identity?.Name ?? "unknown",
                newValue: command.Status.ToString(), details: $"Disconnect for {consumer.AccountNumber}: {request.Reason}");

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                ConnectivityCommandId = command.Id,
                CommandStatus = command.Status.ToString(),
                ConsumerConnectionStatus = consumer.ConnectionStatus.ToString(),
            });
        })
        .WithName("DisconnectConsumer")
        .RequireAuthorization("Operations");


        app.MapPost("/api/v1/consumers/{accountNumber}/reconnect", async (
            string accountNumber,
            ConnectivityRequest request,
            ClaimsPrincipal user,
            PrepaidEngineDbContext db,
            IConnectivityCommandClient connectivityClient) =>
        {
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "A reason is required to reconnect a consumer." });

            var consumer = await db.Consumers.Include(c => c.Wallet).FirstOrDefaultAsync(c => c.AccountNumber == accountNumber);
            if (consumer is null)
                return Results.NotFound();

            if (consumer.ConnectionStatus != ConnectionStatus.Disconnected)
            {
                return Results.Conflict(new { error = $"Cannot reconnect a consumer whose connection status is {consumer.ConnectionStatus}." });
            }

            // Checked up front rather than after a round trip to the meter: dispatching a reconnect
            // command that Consumer.Reconnect() would just reject on Acknowledged serves no one — the
            // real-world precondition (a positive balance) is knowable before involving the meter at all.
            if (consumer.Wallet.Balance <= 0)
            {
                return Results.BadRequest(new { error = "Cannot reconnect a consumer with a zero or negative wallet balance." });
            }

            consumer.RequestReconnection();

            var command = new ConnectivityCommand(Guid.NewGuid(), consumer.Id, ConnectivityCommandType.Reconnect, request.Reason, DateTime.UtcNow);
            db.ConnectivityCommands.Add(command);
            command.MarkSent(DateTime.UtcNow);

            var correlationId = request.CorrelationId ?? $"reconnect-{Guid.NewGuid():N}";
            var result = await connectivityClient.SendConnectivityCommandAsync(
                new SendConnectivityCommandRequest(consumer.Id, ConnectivityCommandType.Reconnect, correlationId));

            switch (result.Outcome)
            {
                case ConnectivityCommandOutcome.Acknowledged:
                    command.MarkAcknowledged(DateTime.UtcNow);
                    consumer.Reconnect();
                    break;
                case ConnectivityCommandOutcome.Failed:
                    command.MarkFailed(result.Message ?? "Meter rejected the reconnect command.");
                    RaiseException(db, OperationalExceptionSourceType.ConnectivityCommand, command.Id, consumer.Id,
                        $"Reconnect command {command.Id} failed: {command.ErrorMessage}");
                    break;
                case ConnectivityCommandOutcome.TimedOut:
                    command.MarkTimedOut();
                    RaiseException(db, OperationalExceptionSourceType.ConnectivityCommand, command.Id, consumer.Id,
                        $"Reconnect command {command.Id} timed out waiting for meter acknowledgement.");
                    break;
            }

            Audit(db, nameof(ConnectivityCommand), command.Id.ToString(), "Dispatched", user.Identity?.Name ?? "unknown",
                newValue: command.Status.ToString(), details: $"Reconnect for {consumer.AccountNumber}: {request.Reason}");

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                ConnectivityCommandId = command.Id,
                CommandStatus = command.Status.ToString(),
                ConsumerConnectionStatus = consumer.ConnectionStatus.ToString(),
            });
        })
        .WithName("ReconnectConsumer")
        .RequireAuthorization("Operations");


        // Recharge flow, orchestrated through IRmsClient (MockRmsClient for now — see the TODO
        // above). RMS remains authoritative: this endpoint only credits the wallet once RMS reports
        // Success, and never re-credits for a repeated idempotency key or a duplicated RMS reference.
        // On RMS Success, also dispatches a MeterCommand through IMeterCommandClient — a separate
        // lifecycle from the RechargeTransaction itself (see MeterCommand's doc comment): the response
        // always distinguishes "RMS confirmed" from "meter acknowledged", never conflating the two.
        app.MapPost("/api/v1/consumers/{accountNumber}/recharge", async (
            string accountNumber,
            RechargeRequest request,
            PrepaidEngineDbContext db,
            IRmsClient rmsClient,
            IEmergencyCreditGuard emergencyCreditGuard,
            ClaimsPrincipal user) =>
        {
            if (request.Amount <= 0)
                return Results.BadRequest(new { error = "Amount must be positive." });

            // Rs. 500 minimum recharge (AMISP integration requirement doc section 6). This endpoint is
            // for genuine top-ups only — an RMS-driven reconciliation adjustment (which can be small or
            // negative by design, per the same spec section) goes through
            // POST /api/v1/consumers/{accountNumber}/reconciliation-adjustments instead, so no exemption
            // is needed here.
            if (request.Amount < MinimumRechargeAmount)
                return Results.BadRequest(new { error = $"Minimum recharge amount is Rs. {MinimumRechargeAmount}." });

            // The idempotency key must come from the caller and stay stable across their own retries
            // of this logical request — a server-generated key would defeat the whole guarantee, since
            // a lost response followed by a client retry would mint a new key and double-recharge.
            if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
                return Results.BadRequest(new { error = "IdempotencyKey is required and must be stable across retries of the same recharge attempt." });

            var consumer = await db.Consumers
                .Include(c => c.Wallet).ThenInclude(w => w.Transactions)
                .FirstOrDefaultAsync(c => c.AccountNumber == accountNumber);
            if (consumer is null)
                return Results.NotFound();

            var idempotencyKey = request.IdempotencyKey;
            var correlationId = Guid.NewGuid().ToString("N");

            RmsRechargeResult rmsResult;
            try
            {
                rmsResult = await rmsClient.InitiateRechargeAsync(
                    new RmsRechargeRequest(consumer.Id, request.Amount, idempotencyKey, correlationId));
            }
            catch (RmsUnavailableException ex)
            {
                return Results.Problem(statusCode: StatusCodes.Status503ServiceUnavailable,
                    title: "RMS unavailable", detail: ex.Message);
            }

            // Our own idempotency guard: never re-credit for an RMS reference we've already recorded,
            // even if this call raced with another request for the same idempotency key.
            var existing = await db.RechargeTransactions
                .FirstOrDefaultAsync(r => r.RmsReferenceId == rmsResult.RmsReferenceId);
            if (existing is not null)
            {
                var existingCommand = await db.MeterCommands.FirstOrDefaultAsync(m => m.RechargeTransactionId == existing.Id);
                return Results.Ok(new
                {
                    existing.RmsReferenceId,
                    Status = existing.Status.ToString(),
                    WalletBalance = consumer.Wallet.Balance,
                    MeterCommandStatus = existingCommand?.Status.ToString(),
                    Replayed = true
                });
            }

            var recharge = new RechargeTransaction(Guid.NewGuid(), consumer.Id, request.Amount, rmsResult.RmsReferenceId, DateTime.UtcNow);
            db.RechargeTransactions.Add(recharge);

            switch (rmsResult.Status)
            {
                case RmsRechargeStatus.Success:
                    recharge.MarkSuccessful(DateTime.UtcNow);
                    // Explicitly track the new ledger entry as Added: the wallet was loaded from the
                    // DB (already tracked, not part of a brand-new graph), and EF's change detection
                    // does not reliably infer "newly added" for an entity appended to an
                    // already-tracked entity's backing-field collection — it can mis-detect it as
                    // Modified and emit a bogus UPDATE for a row that doesn't exist yet.
                    var walletTransaction = consumer.Wallet.Credit(request.Amount, WalletTransactionType.Recharge, rmsResult.RmsReferenceId);
                    db.WalletTransactions.Add(walletTransaction);

                    // Queue the meter credit command in the same transaction as the wallet credit. RMS confirming payment
                    // does not by itself mean the meter was credited: the MeterCommandWorker delivers the command and
                    // only its acknowledgement makes that true, per MeterCommand's own state machine. The recharge
                    // never waits on the meter/MDM layer.
                    var meterCommand = new MeterCommand(Guid.NewGuid(), consumer.Id, recharge.Id, request.Amount, DateTime.UtcNow);
                    db.MeterCommands.Add(meterCommand);

                    // The wallet was credited above regardless of the meter command's own outcome — check
                    // for an auto-reconnect independently of whether the meter command itself succeeded.
                    await emergencyCreditGuard.EvaluateAsync(consumer);

                    Audit(db, nameof(RechargeTransaction), recharge.Id.ToString(), "RECHARGE_COMPLETED", user.Identity?.Name ?? "unknown",
                        newValue: $"Rs.{recharge.Amount}", details: $"Account {consumer.AccountNumber}, RMS ref {recharge.RmsReferenceId}, meter credit {meterCommand.Status}.");
                    await db.SaveChangesAsync();
                    return Results.Ok(new
                    {
                        recharge.RmsReferenceId,
                        Status = recharge.Status.ToString(),
                        WalletBalance = consumer.Wallet.Balance,
                        MeterCommandStatus = meterCommand.Status.ToString(),
                    });

                case RmsRechargeStatus.Failed:
                    recharge.MarkFailed(DateTime.UtcNow);
                    Audit(db, nameof(RechargeTransaction), recharge.Id.ToString(), "RECHARGE_FAILED", user.Identity?.Name ?? "unknown",
                        newValue: $"Rs.{recharge.Amount}", details: $"Account {consumer.AccountNumber}, RMS ref {recharge.RmsReferenceId}: {rmsResult.Message}");
                    await db.SaveChangesAsync();
                    return Results.Json(
                        new { recharge.RmsReferenceId, Status = recharge.Status.ToString(), rmsResult.Message },
                        statusCode: StatusCodes.Status402PaymentRequired);

                case RmsRechargeStatus.Pending:
                    Audit(db, nameof(RechargeTransaction), recharge.Id.ToString(), "RECHARGE_PENDING", user.Identity?.Name ?? "unknown",
                        newValue: $"Rs.{recharge.Amount}", details: $"Account {consumer.AccountNumber}, RMS ref {recharge.RmsReferenceId}.");
                    await db.SaveChangesAsync();
                    return Results.Accepted(value: new { recharge.RmsReferenceId, Status = recharge.Status.ToString(), rmsResult.Message });

                default:
                    // Explicitly reject any status this endpoint doesn't know how to handle yet,
                    // rather than silently treating it as Pending.
                    return Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
                        title: "Unhandled RMS recharge status", detail: $"No handling defined for RMS status '{rmsResult.Status}'.");
            }
        })
        .WithName("RechargeConsumer")
        .RequireAuthorization("Operations");


        // --- Billing reconciliation: RMS-pushed signed adjustments (spec sections 7 & 8) applied to ---
        // the consumer's wallet exactly like a recharge, plus the daily billing-data export AMISP needs
        // to give RMS so it can compute its own shadow monthly bill and find any gap in the first place.
        app.MapPost("/api/v1/consumers/{accountNumber}/reconciliation-adjustments", async (
            string accountNumber, ReconciliationAdjustmentRequest request, ClaimsPrincipal user, PrepaidEngineDbContext db,
            IEmergencyCreditGuard emergencyCreditGuard) =>
        {
            if (request.Amount == 0)
                return Results.BadRequest(new { error = "A reconciliation adjustment amount cannot be zero." });
            if (string.IsNullOrWhiteSpace(request.Reference))
                return Results.BadRequest(new { error = "A reference is required for a reconciliation adjustment." });

            var consumer = await db.Consumers.Include(c => c.Wallet).ThenInclude(w => w.Transactions)
                .FirstOrDefaultAsync(c => c.AccountNumber == accountNumber);
            if (consumer is null)
                return Results.NotFound();

            var adjustment = ApplyReconciliationAdjustment(db, consumer, request.Amount, request.ReconciliationDate, request.Reference);

            Audit(db, nameof(ReconciliationAdjustment), adjustment.Id.ToString(), "Applied", user.Identity?.Name ?? "unknown",
                newValue: adjustment.Amount.ToString("0.00"), details: $"For {consumer.AccountNumber}: {request.Reference}");

            // A reconciliation adjustment can move the balance in either direction — let the guard decide
            // whether that crossed the emergency-credit line one way or the other.
            await emergencyCreditGuard.EvaluateAsync(consumer);

            await db.SaveChangesAsync();

            return Results.Ok(new
            {
                adjustment.Id,
                adjustment.Amount,
                adjustment.PaymentMode,
                adjustment.ReconciliationDate,
                adjustment.Reference,
                adjustment.BalanceAfter,
                adjustment.AppliedAt,
            });
        })
        .WithName("ApplyReconciliationAdjustment")
        .RequireAuthorization("Operations");


        app.MapPost("/api/v1/consumers/{consumerId:guid}/meter-replacement", async (
            Guid consumerId, MeterReplacementApiRequest request, IBillingEngineService billingEngine, PrepaidEngineDbContext db, ClaimsPrincipal user) =>
        {
            if (string.IsNullOrWhiteSpace(request.NewMeterNumber))
                return Results.BadRequest(new { error = "A new meter number is required." });
            if (string.IsNullOrWhiteSpace(request.Reason))
                return Results.BadRequest(new { error = "A reason is required for a meter replacement." });

            try
            {
                var assignment = await billingEngine.ReplaceMeterAsync(consumerId, new MeterReplacementRequest(
                    request.NewMeterNumber, request.Phase, request.EffectiveFrom,
                    request.OldMeterClosingReadingKwh, request.NewMeterOpeningReadingKwh, request.Reason));

                Audit(db, nameof(MeterAssignment), assignment.Id.ToString(), "METER_REPLACED", user.Identity?.Name ?? "unknown",
                    oldValue: assignment.OldMeterId.ToString(), newValue: assignment.NewMeterId.ToString(), details: request.Reason);
                await db.SaveChangesAsync();

                return Results.Ok(new
                {
                    assignment.Id,
                    assignment.ConsumerId,
                    assignment.OldMeterId,
                    assignment.NewMeterId,
                    assignment.EventType,
                    assignment.EffectiveFrom,
                    assignment.OldMeterClosingReadingKwh,
                    assignment.NewMeterOpeningReadingKwh,
                    assignment.Reason,
                });
            }
            catch (InvalidOperationException ex)
            {
                return Results.NotFound(new { error = ex.Message });
            }
        })
        .WithName("ReplaceMeter")
        .RequireAuthorization("Operations");


        app.MapGet("/api/v1/consumers/{consumerId:guid}/notifications", async (Guid consumerId, PrepaidEngineDbContext db) =>
        {
            var notifications = await db.NotificationEvents
                .Where(n => n.ConsumerId == consumerId)
                .OrderByDescending(n => n.CreatedAt)
                .Select(n => new
                {
                    n.Id,
                    n.EventType,
                    n.Message,
                    n.Status,
                    n.CreatedAt,
                    n.SentAt,
                    n.ProviderReference,
                })
                .ToListAsync();

            return Results.Ok(notifications);
        })
        .WithName("GetConsumerNotifications")
        .RequireAuthorization();

        // Capture or correct a consumer's notification mobile number. The number is normalised to +91XXXXXXXXXX, and
        // only its last four digits go into the audit trail.
        app.MapPut("/api/v1/consumers/{accountNumber}/mobile", async (
            string accountNumber, UpdateMobileRequest request, PrepaidEngineDbContext db, ClaimsPrincipal user) =>
        {
            if (!MobileNumber.TryNormalize(request.MobileNumber, out var normalized))
                return Results.BadRequest(new { error = "Enter a valid 10-digit Indian mobile number (starting 6-9), with or without +91." });

            var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.AccountNumber == accountNumber);
            if (consumer is null)
                return Results.NotFound();

            var previous = consumer.MobileNumber;
            if (previous != normalized)
            {
                consumer.SetMobileNumber(normalized);
                Audit(db, nameof(Consumer), consumer.Id.ToString(), "MOBILE_UPDATED", user.Identity?.Name ?? "unknown",
                    oldValue: previous is null ? null : MobileNumber.Mask(previous), newValue: MobileNumber.Mask(normalized));
                await db.SaveChangesAsync();
            }
            return Results.Ok(new { consumer.AccountNumber, consumer.MobileNumber });
        })
        .WithName("UpdateConsumerMobile")
        .RequireAuthorization("Operations");
    }
}
