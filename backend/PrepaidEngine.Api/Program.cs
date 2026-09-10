using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Auth;
using PrepaidEngine.Application.MeterCommands;
using PrepaidEngine.Application.Rms;
using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.MeterCommands;
using PrepaidEngine.Infrastructure.Persistence;
using PrepaidEngine.Infrastructure.Persistence.Seed;
using PrepaidEngine.Infrastructure.Rms;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddDbContext<PrepaidEngineDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("PrepaidEngine")));

// TODO(RMS integration): swap for a real HTTP-based IRmsClient adapter once RMS's API
// contract is available; keep MockRmsClient registered for local dev / tests until then.
builder.Services.AddSingleton<IRmsClient, MockRmsClient>();

// TODO(meter-command integration): swap for a real adapter (STS/DLMS/COSEM/vendor API) once
// one exists; keep MockMeterCommandClient registered for local dev / tests until then.
builder.Services.AddSingleton<IMeterCommandClient, MockMeterCommandClient>();

// Basic auth for the demo endpoints only — a stop-gap, not a substitute for real
// authentication before any shared/production exposure (see docs/assumptions-and-security.md).
builder.Services.AddAuthentication("Basic")
    .AddScheme<BasicAuthenticationSchemeOptions, BasicAuthenticationHandler>("Basic", null);
builder.Services.AddAuthorization();

// Local-dev-only CORS so the Angular dev server (ng serve, default port 4200) can call this
// API cross-origin. Never widen this beyond the dev server's own origin, and never enable it
// outside Development — see docs/assumptions-and-security.md.
const string AngularDevCorsPolicy = "AngularDev";
builder.Services.AddCors(options =>
{
    options.AddPolicy(AngularDevCorsPolicy, policy =>
        policy.WithOrigins("http://localhost:4200")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Local/demo convenience only: apply any pending migrations and seed sample data.
    // Never runs outside Development, so production deployments must migrate explicitly.
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PrepaidEngineDbContext>();
    await db.Database.MigrateAsync();
    await DbSeeder.SeedAsync(db);

    app.UseCors(AngularDevCorsPolicy);
}

app.UseHttpsRedirection();

// Minimal hand-built demo console (wwwroot/index.html) that drives the recharge flow against
// this same API — static files only, no build step, no framework. It is not the Angular
// frontend (still paused, see README) and calls the API endpoints below directly, using the
// same Basic auth the API itself enforces (entered by the demo user, never hard-coded here).
app.UseDefaultFiles();
app.UseStaticFiles();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("Health");

// Demo/local-only read endpoints, behind HTTP Basic auth (DemoAuth:Username/Password, set
// via user-secrets). This is a stop-gap for a local demo, not a substitute for real
// authentication/authorization before any shared or production exposure — see
// docs/assumptions-and-security.md.
app.MapGet("/api/v1/consumers", async (PrepaidEngineDbContext db) =>
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
        .ToListAsync();

    return Results.Ok(consumers);
})
.WithName("ListConsumers")
.RequireAuthorization();

app.MapGet("/api/v1/consumers/{accountNumber}", async (string accountNumber, PrepaidEngineDbContext db) =>
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

    return Results.Ok(new
    {
        consumer.AccountNumber,
        consumer.Name,
        consumer.ServiceAddress,
        consumer.ConnectionStatus,
        consumer.ConnectedLoadKw,
        Meter = new { consumer.Meter.MeterNumber, consumer.Meter.Phase, consumer.Meter.LastReadingKwh },
        Wallet = new
        {
            consumer.Wallet.Balance,
            consumer.Wallet.EmergencyCreditLimit,
            consumer.Wallet.IsWithinEmergencyCredit,
            Transactions = consumer.Wallet.Transactions.Select(t => new { t.Amount, t.Type, t.OccurredAt, t.Reference })
        },
        Bills = bills
    });
})
.WithName("GetConsumerByAccountNumber")
.RequireAuthorization();

// Billing dashboard read endpoints — real data across all consumers, joined with the tariff
// and consumption reading each bill was generated from. Demo/local-only, same Basic-auth
// stop-gap as every other endpoint above (see docs/assumptions-and-security.md).
app.MapGet("/api/v1/bills", async (PrepaidEngineDbContext db) =>
{
    var bills = await db.Bills
        .Join(db.Consumers, b => b.ConsumerId, c => c.Id, (b, c) => new { Bill = b, Consumer = c })
        .Join(db.Tariffs, x => x.Bill.TariffId, t => t.Id, (x, t) => new { x.Bill, x.Consumer, Tariff = t })
        .OrderByDescending(x => x.Bill.GeneratedAt)
        .Select(x => new
        {
            x.Bill.Id,
            x.Consumer.AccountNumber,
            x.Consumer.Name,
            Category = x.Tariff.Category,
            TariffName = x.Tariff.Name,
            x.Bill.EnergyChargeGross,
            x.Bill.PrepaidRebateAmount,
            EnergyChargeNet = x.Bill.EnergyChargeGross - x.Bill.PrepaidRebateAmount,
            x.Bill.FixedCharge,
            x.Bill.ElectricityDutyAmount,
            x.Bill.FppasAmount,
            x.Bill.TmcAmount,
            x.Bill.CpmcAmount,
            x.Bill.ArrearsAmount,
            x.Bill.Amount,
            x.Bill.AmountPaid,
            x.Bill.Status,
            x.Bill.GeneratedAt,
        })
        .ToListAsync();

    return Results.Ok(bills);
})
.WithName("ListBills")
.RequireAuthorization();

app.MapGet("/api/v1/bills/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
{
    var bill = await db.Bills.FirstOrDefaultAsync(b => b.Id == id);
    if (bill is null)
        return Results.NotFound();

    var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.Id == bill.ConsumerId);
    var tariff = await db.Tariffs.Include(t => t.Slabs).FirstOrDefaultAsync(t => t.Id == bill.TariffId);
    var reading = await db.ConsumptionReadings.FirstOrDefaultAsync(r => r.Id == bill.ConsumptionReadingId);

    if (consumer is null || tariff is null || reading is null)
    {
        // Data integrity issue (a bill referencing a deleted consumer/tariff/reading) rather
        // than a legitimate "not found" for the bill itself — surface it distinctly.
        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Bill references missing data",
            detail: $"Bill {id} references a consumer, tariff, or consumption reading that no longer exists.");
    }

    return Results.Ok(new
    {
        bill.Id,
        Consumer = new { consumer.AccountNumber, consumer.Name },
        Tariff = new { tariff.Id, tariff.Name, tariff.Category, tariff.FixedChargePerUnitPerMonth, tariff.PrepaidEnergyRebatePercent },
        Reading = new { reading.ConsumptionKwh, reading.PeriodStart, reading.PeriodEnd },
        bill.EnergyChargeGross,
        bill.PrepaidRebateAmount,
        EnergyChargeNet = bill.EnergyChargeGross - bill.PrepaidRebateAmount,
        bill.FixedCharge,
        bill.ElectricityDutyAmount,
        bill.FppasAmount,
        bill.FppasChargeId,
        bill.TmcAmount,
        bill.CpmcAmount,
        bill.ArrearsAmount,
        bill.ArrearsRecovered,
        bill.Amount,
        bill.AmountPaid,
        bill.Status,
        bill.GeneratedAt,
    });
})
.WithName("GetBillById")
.RequireAuthorization();

// Tariff Management read endpoints — the real Tariff/TariffSlab/TouPeriod configuration this
// engine actually bills against (see docs/tariff-validation-report.md for sourcing). Read-only
// for now: no create/update endpoint exists yet, since tariff changes need versioning/approval
// workflow (see the UI/UX spec's "never silently overwrite an active tariff" rule) that this
// project hasn't built — better to expose nothing than a naive PUT that violates it.
app.MapGet("/api/v1/tariffs", async (PrepaidEngineDbContext db) =>
{
    var tariffs = await db.Tariffs
        .Select(t => new
        {
            t.Id,
            t.Name,
            t.Category,
            t.FixedChargePerUnitPerMonth,
            t.PrepaidEnergyRebatePercent,
            t.EmergencyCreditLimit,
            SlabCount = t.Slabs.Count,
            TouPeriodCount = t.TouPeriods.Count,
        })
        .ToListAsync();

    return Results.Ok(tariffs);
})
.WithName("ListTariffs")
.RequireAuthorization();

app.MapGet("/api/v1/tariffs/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
{
    var tariff = await db.Tariffs
        .Include(t => t.Slabs)
        .Include(t => t.TouPeriods)
        .FirstOrDefaultAsync(t => t.Id == id);

    if (tariff is null)
        return Results.NotFound();

    return Results.Ok(new
    {
        tariff.Id,
        tariff.Name,
        tariff.Category,
        tariff.FixedChargePerUnitPerMonth,
        tariff.PrepaidEnergyRebatePercent,
        tariff.EmergencyCreditLimit,
        tariff.MinVendAmountSinglePhase,
        tariff.MaxVendAmountSinglePhase,
        tariff.MinVendAmountThreePhase,
        tariff.MaxVendAmountThreePhase,
        Slabs = tariff.Slabs
            .OrderBy(s => s.FromKwh)
            .Select(s => new { s.Id, s.FromKwh, s.UpToKwh, s.RatePerKwh }),
        TouPeriods = tariff.TouPeriods
            .OrderBy(p => p.StartTime)
            .Select(p => new { p.Id, p.Label, StartTime = p.StartTime.ToString(), EndTime = p.EndTime.ToString(), p.RatePerKvah }),
    });
})
.WithName("GetTariffById")
.RequireAuthorization();

// Calculation Workbench — a SIMULATION-ONLY preview of a charge calculation for an arbitrary
// (tariff, consumption, load) combination, not tied to any real consumer/bill. Delegates every
// figure to the same domain methods (Tariff.CalculateEnergyCharge/CalculateFixedCharge/
// CalculateDailyFixedCharge, ElectricityDuty.Calculate) that production billing uses — the
// frontend must not duplicate this arithmetic itself (see docs/frontend-scope.md's frontend
// calculation rule), it only renders whatever this endpoint returns.
app.MapPost("/api/v1/calculation-workbench/simulate", async (SimulateChargeRequest request, PrepaidEngineDbContext db) =>
{
    if (request.ConsumptionKwh < 0)
        return Results.BadRequest(new { error = "Consumption cannot be negative." });
    if (request.ConnectedLoadOrContractDemand < 0)
        return Results.BadRequest(new { error = "Connected load / contract demand cannot be negative." });

    var tariff = await db.Tariffs.Include(t => t.Slabs).FirstOrDefaultAsync(t => t.Id == request.TariffId);
    if (tariff is null)
        return Results.NotFound(new { error = $"No tariff found with id '{request.TariffId}'." });

    if (tariff.Slabs.Count == 0)
    {
        // Pure-ToD tariffs (IHT/IEHT) have no ordinary kWh slabs — CalculateEnergyCharge would
        // silently return 0 for them, which would misrepresent a real charge as zero rather
        // than reporting that this simulator doesn't support ToD-only tariffs yet.
        return Results.BadRequest(new
        {
            error = $"Tariff '{tariff.Name}' has no ordinary energy slabs (it is ToD-only) — this simulator does not yet support ToD-based simulation.",
        });
    }

    var grossEnergyCharge = tariff.CalculateEnergyCharge(request.ConsumptionKwh);
    var rebateAmount = grossEnergyCharge * (tariff.PrepaidEnergyRebatePercent / 100m);
    var netEnergyCharge = grossEnergyCharge - rebateAmount;
    var fixedChargeMonthly = tariff.CalculateFixedCharge(request.ConnectedLoadOrContractDemand);
    var fixedChargeDaily = tariff.CalculateDailyFixedCharge(request.ConnectedLoadOrContractDemand);
    var electricityDuty = ElectricityDuty.Calculate(tariff.Category, request.ConsumptionKwh);
    var totalMonthlyCharge = netEnergyCharge + fixedChargeMonthly + electricityDuty;

    return Results.Ok(new
    {
        Simulation = true,
        Tariff = new { tariff.Id, tariff.Name, tariff.Category },
        Inputs = new { request.ConsumptionKwh, request.ConnectedLoadOrContractDemand },
        GrossEnergyCharge = grossEnergyCharge,
        PrepaidRebatePercent = tariff.PrepaidEnergyRebatePercent,
        RebateAmount = rebateAmount,
        NetEnergyCharge = netEnergyCharge,
        FixedChargeMonthly = fixedChargeMonthly,
        FixedChargeDaily = fixedChargeDaily,
        ElectricityDuty = electricityDuty,
        TotalMonthlyCharge = totalMonthlyCharge,
    });
})
.WithName("SimulateCharge")
.RequireAuthorization();

// Recharge Operations read endpoints — real RechargeTransaction records across all consumers.
// Note: RechargeStatus has no explicit "Pending" value — the RMS-Pending branch of the POST
// endpoint below deliberately leaves a transaction in its initial "Initiated" state (RMS
// hasn't told us Success or Failed yet), so "Initiated" here doubles as "Pending" in the UI.
app.MapGet("/api/v1/recharges", async (PrepaidEngineDbContext db) =>
{
    // Left join to MeterCommands: a recharge that never reached RMS Success (or hasn't been
    // dispatched to the meter yet) legitimately has no command row — that must render as "no
    // meter command exists", not be dropped from the list or crash the query.
    var recharges = await (
        from r in db.RechargeTransactions
        join c in db.Consumers on r.ConsumerId equals c.Id
        join mcOuter in db.MeterCommands on r.Id equals mcOuter.RechargeTransactionId into mcGroup
        from mc in mcGroup.DefaultIfEmpty()
        orderby r.InitiatedAt descending
        select new
        {
            r.Id,
            c.AccountNumber,
            c.Name,
            r.Amount,
            r.RmsReferenceId,
            r.Status,
            r.InitiatedAt,
            r.CompletedAt,
            MeterCommandStatus = mc == null ? (MeterCommandStatus?)null : mc.Status,
        })
        .ToListAsync();

    return Results.Ok(recharges);
})
.WithName("ListRecharges")
.RequireAuthorization();

app.MapGet("/api/v1/recharges/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
{
    var recharge = await db.RechargeTransactions.FirstOrDefaultAsync(r => r.Id == id);
    if (recharge is null)
        return Results.NotFound();

    var consumer = await db.Consumers.Include(c => c.Wallet).FirstOrDefaultAsync(c => c.Id == recharge.ConsumerId);
    if (consumer is null)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Recharge references missing data",
            detail: $"Recharge {id} references a consumer that no longer exists.");
    }

    var meterCommand = await db.MeterCommands.FirstOrDefaultAsync(m => m.RechargeTransactionId == id);

    return Results.Ok(new
    {
        recharge.Id,
        Consumer = new { consumer.AccountNumber, consumer.Name },
        recharge.Amount,
        recharge.RmsReferenceId,
        recharge.Status,
        recharge.InitiatedAt,
        recharge.CompletedAt,
        WalletBalance = consumer.Wallet.Balance,
        MeterCommand = meterCommand is null ? null : new
        {
            meterCommand.Id,
            meterCommand.Status,
            meterCommand.RetryCount,
            meterCommand.ErrorMessage,
            meterCommand.CreatedAt,
            meterCommand.SentAt,
            meterCommand.AcknowledgedAt,
        },
    });
})
.WithName("GetRechargeById")
.RequireAuthorization();

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
    IMeterCommandClient meterCommandClient) =>
{
    if (request.Amount <= 0)
        return Results.BadRequest(new { error = "Amount must be positive." });

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

            // Dispatch the meter credit command. RMS confirming payment does not by itself mean
            // the meter was credited — that only becomes true if/when MarkAcknowledged() below
            // actually runs, per MeterCommand's own state machine.
            var meterCommand = new MeterCommand(Guid.NewGuid(), consumer.Id, recharge.Id, request.Amount, DateTime.UtcNow);
            db.MeterCommands.Add(meterCommand);
            meterCommand.MarkSent(DateTime.UtcNow);

            var meterResult = await meterCommandClient.SendCreditCommandAsync(
                new SendCreditCommandRequest(consumer.Id, request.Amount, request.IdempotencyKey));

            switch (meterResult.Outcome)
            {
                case MeterCommandOutcome.Acknowledged:
                    meterCommand.MarkAcknowledged(DateTime.UtcNow);
                    break;
                case MeterCommandOutcome.Failed:
                    meterCommand.MarkFailed(meterResult.Message ?? "Meter rejected the credit command.");
                    break;
                case MeterCommandOutcome.TimedOut:
                    meterCommand.MarkTimedOut();
                    break;
            }

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
            await db.SaveChangesAsync();
            return Results.Json(
                new { recharge.RmsReferenceId, Status = recharge.Status.ToString(), rmsResult.Message },
                statusCode: StatusCodes.Status402PaymentRequired);

        case RmsRechargeStatus.Pending:
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
.RequireAuthorization();

app.Run();

/// <param name="Amount">Recharge amount.</param>
/// <param name="IdempotencyKey">
/// Required. Caller-supplied and must stay the same across retries of this exact recharge
/// attempt — the server deliberately does not generate one, since a server-generated key
/// would not survive a client retry after a lost response, defeating the whole guarantee.
/// In the mock RMS, prefix this with "FAIL-", "PENDING-", or "UNAVAILABLE-" to demo those outcomes.
/// </param>
public record RechargeRequest(decimal Amount, string? IdempotencyKey = null);

/// <param name="TariffId">The tariff to simulate against — must have at least one ordinary energy slab.</param>
/// <param name="ConsumptionKwh">Hypothetical consumption for this simulation.</param>
/// <param name="ConnectedLoadOrContractDemand">Hypothetical connected load/contract demand.</param>
public record SimulateChargeRequest(Guid TariffId, decimal ConsumptionKwh, decimal ConnectedLoadOrContractDemand);

// Exposed so WebApplicationFactory-based integration tests can bootstrap this Api project.
public partial class Program { }
