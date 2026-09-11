using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Auth;
using PrepaidEngine.Application.Connectivity;
using PrepaidEngine.Application.MeterCommands;
using PrepaidEngine.Application.Rms;
using PrepaidEngine.Domain;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.Connectivity;
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

// TODO(connectivity-command integration): swap for a real adapter once one exists; keep
// MockConnectivityCommandClient registered for local dev / tests until then.
builder.Services.AddSingleton<IConnectivityCommandClient, MockConnectivityCommandClient>();

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

// --- Local helpers: audit / operational-exception / reconciliation wiring ------------------
// These are plain local functions (no external port needed) called from the endpoints below at
// the specific, documented trigger points (see README.md's Audit and Reconciliation sections).

/// <summary>Minimum genuine top-up recharge amount (Rs.), per AMISP integration requirement doc
/// section 6. Does not apply to reconciliation adjustments — see the recharge endpoint below.</summary>
const decimal MinimumRechargeAmount = 500m;

/// <summary>"Happy Hours" during which a disconnect command must NEVER be dispatched — daily from
/// 2:00 PM to 9:00 AM IST, per spec section 5. Disconnection is only allowed in the 9:00 AM-2:00
/// PM IST window. The spec's hours are Meghalaya wall-clock (India Standard Time, UTC+5:30) —
/// every other timestamp in this codebase is stored/compared as UTC, so this converts explicitly
/// with a fixed offset (IST has no DST, so no OS timezone database lookup is needed) rather than
/// relying on <c>DateTime.Now</c>, which would silently use the server's local timezone and give
/// the wrong answer on any host not itself set to IST (most cloud/container deployments default
/// to UTC). NOTE: the spec also exempts all public holidays on the Nagaland State Govt calendar;
/// this project has no holiday-calendar concept, so only the daily time window is enforced here —
/// a real holiday calendar is a known, documented gap (see README).</summary>
static bool IsWithinDisconnectWindow(DateTime utcNow)
{
    var istHour = utcNow.Add(TimeSpan.FromHours(5.5)).Hour;
    return istHour >= 9 && istHour < 14;
}

static AuditEntry Audit(PrepaidEngineDbContext db, string entityType, string entityId, string action, string actor,
    string? oldValue = null, string? newValue = null, string? details = null)
{
    var entry = new AuditEntry(Guid.NewGuid(), entityType, entityId, action, actor, DateTime.UtcNow, oldValue, newValue, details);
    db.AuditEntries.Add(entry);
    return entry;
}

static OperationalException RaiseException(PrepaidEngineDbContext db, OperationalExceptionSourceType sourceType, Guid sourceId, Guid consumerId, string description)
{
    var exception = new OperationalException(Guid.NewGuid(), sourceType, sourceId, consumerId, description, DateTime.UtcNow);
    db.OperationalExceptions.Add(exception);
    return exception;
}

// Applies a ReconciliationAdjustment (RMS-pushed signed amount, spec sections 7-8) to a
// consumer's wallet exactly like a recharge credit/debit, tagged distinctly in the ledger. Shared
// by the reconciliation endpoint below — kept as a local function so the wallet-mutation logic
// lives in exactly one place rather than being duplicated alongside the recharge endpoint's own.
static ReconciliationAdjustment ApplyReconciliationAdjustment(
    PrepaidEngineDbContext db, Consumer consumer, decimal amount, DateTime reconciliationDate, string reference)
{
    if (amount > 0)
        consumer.Wallet.Credit(amount, WalletTransactionType.Reconciliation, reference);
    else
        consumer.Wallet.Debit(-amount, WalletTransactionType.Reconciliation, reference);

    var adjustment = new ReconciliationAdjustment(
        Guid.NewGuid(), consumer.Id, consumer.AccountNumber, amount, reconciliationDate, reference,
        balanceAfter: consumer.Wallet.Balance, appliedAt: DateTime.UtcNow);
    db.ReconciliationAdjustments.Add(adjustment);
    return adjustment;
}

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

    return Results.Ok(new
    {
        consumer.AccountNumber,
        consumer.Name,
        consumer.ServiceAddress,
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

    Audit(db, nameof(ConnectivityCommand), command.Id.ToString(), "Dispatched", "system",
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
.RequireAuthorization();

app.MapPost("/api/v1/consumers/{accountNumber}/reconnect", async (
    string accountNumber,
    ConnectivityRequest request,
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

    Audit(db, nameof(ConnectivityCommand), command.Id.ToString(), "Dispatched", "system",
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
.RequireAuthorization();

// RC/DC read endpoints — real ConnectivityCommand records across all consumers.
app.MapGet("/api/v1/connectivity-commands", async (PrepaidEngineDbContext db) =>
{
    var commands = await (
        from c in db.ConnectivityCommands
        join consumer in db.Consumers on c.ConsumerId equals consumer.Id
        orderby c.CreatedAt descending
        select new
        {
            c.Id,
            consumer.AccountNumber,
            consumer.Name,
            c.CommandType,
            c.Reason,
            c.Status,
            c.RetryCount,
            c.ErrorMessage,
            c.CreatedAt,
            c.SentAt,
            c.AcknowledgedAt,
        })
        .ToListAsync();

    return Results.Ok(commands);
})
.WithName("ListConnectivityCommands")
.RequireAuthorization();

app.MapGet("/api/v1/connectivity-commands/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
{
    var command = await db.ConnectivityCommands.FirstOrDefaultAsync(c => c.Id == id);
    if (command is null)
        return Results.NotFound();

    var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.Id == command.ConsumerId);
    if (consumer is null)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Connectivity command references missing data",
            detail: $"Connectivity command {id} references a consumer that no longer exists.");
    }

    return Results.Ok(new
    {
        command.Id,
        Consumer = new { consumer.AccountNumber, consumer.Name, consumer.ConnectionStatus },
        command.CommandType,
        command.Reason,
        command.Status,
        command.RetryCount,
        command.ErrorMessage,
        command.CreatedAt,
        command.SentAt,
        command.AcknowledgedAt,
    });
})
.WithName("GetConnectivityCommandById")
.RequireAuthorization();

// Retries a Failed/TimedOut connectivity command: resets it to Queued via
// ConnectivityCommand.Retry(), then dispatches it again through IConnectivityCommandClient
// exactly like the original attempt — never fabricates a retry result. On a real acknowledgement
// this time, advances the consumer's connection status just like the original dispatch would have.
app.MapPost("/api/v1/connectivity-commands/{id:guid}/retry", async (
    Guid id,
    PrepaidEngineDbContext db,
    IConnectivityCommandClient connectivityClient) =>
{
    var command = await db.ConnectivityCommands.FirstOrDefaultAsync(c => c.Id == id);
    if (command is null)
        return Results.NotFound();

    var consumer = await db.Consumers.Include(c => c.Wallet).FirstOrDefaultAsync(c => c.Id == command.ConsumerId);
    if (consumer is null)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Connectivity command references missing data",
            detail: $"Connectivity command {id} references a consumer that no longer exists.");
    }

    // Re-checked here, not just at the original dispatch: a reconnect command can sit
    // Failed/TimedOut for a while before being retried, and the wallet balance that justified
    // it originally may no longer hold by the time someone clicks Retry (e.g. a new bill
    // debited it back to zero). Without this, a real acknowledgement below would mark the
    // command Acknowledged while leaving the consumer stuck in ReconnectionPending forever,
    // with nothing surfaced to explain why.
    if (command.CommandType == ConnectivityCommandType.Reconnect && consumer.Wallet.Balance <= 0)
    {
        return Results.BadRequest(new { error = "Cannot retry a reconnect for a consumer with a zero or negative wallet balance." });
    }

    // "Happy Hours" — a disconnect retry must be re-checked against the current time just like
    // the original dispatch (spec section 5); a Failed/TimedOut disconnect can sit around for a
    // while before someone clicks Retry.
    if (command.CommandType == ConnectivityCommandType.Disconnect && !IsWithinDisconnectWindow(DateTime.UtcNow))
    {
        return Results.BadRequest(new { error = "Disconnection can only be dispatched between 9:00 AM and 2:00 PM (Happy Hours)." });
    }

    try
    {
        command.Retry();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }

    command.MarkSent(DateTime.UtcNow);

    var correlationId = $"retry-{command.RetryCount}-{Guid.NewGuid():N}";
    var result = await connectivityClient.SendConnectivityCommandAsync(
        new SendConnectivityCommandRequest(command.ConsumerId, command.CommandType, correlationId));

    switch (result.Outcome)
    {
        case ConnectivityCommandOutcome.Acknowledged:
            command.MarkAcknowledged(DateTime.UtcNow);
            if (command.CommandType == ConnectivityCommandType.Disconnect)
                consumer.Disconnect();
            else if (consumer.Wallet.Balance > 0)
                consumer.Reconnect();
            break;
        case ConnectivityCommandOutcome.Failed:
            command.MarkFailed(result.Message ?? "Meter rejected the command.");
            RaiseException(db, OperationalExceptionSourceType.ConnectivityCommand, command.Id, consumer.Id,
                $"Connectivity command {command.Id} failed on retry #{command.RetryCount}: {command.ErrorMessage}");
            break;
        case ConnectivityCommandOutcome.TimedOut:
            command.MarkTimedOut();
            RaiseException(db, OperationalExceptionSourceType.ConnectivityCommand, command.Id, consumer.Id,
                $"Connectivity command {command.Id} timed out on retry #{command.RetryCount}.");
            break;
    }

    Audit(db, nameof(ConnectivityCommand), command.Id.ToString(), "Retried", "system",
        newValue: command.Status.ToString(), details: $"Retry #{command.RetryCount} for {consumer.AccountNumber}");

    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        command.Id,
        command.Status,
        command.RetryCount,
        command.ErrorMessage,
        ConsumerConnectionStatus = consumer.ConnectionStatus.ToString(),
    });
})
.WithName("RetryConnectivityCommand")
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
                    RaiseException(db, OperationalExceptionSourceType.MeterCommand, meterCommand.Id, consumer.Id,
                        $"Meter command {meterCommand.Id} failed: {meterCommand.ErrorMessage}");
                    break;
                case MeterCommandOutcome.TimedOut:
                    meterCommand.MarkTimedOut();
                    RaiseException(db, OperationalExceptionSourceType.MeterCommand, meterCommand.Id, consumer.Id,
                        $"Meter command {meterCommand.Id} timed out waiting for meter acknowledgement.");
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

// Meter Credit read endpoints — real MeterCommand records across all consumers, each traceable
// back to the RechargeTransaction that triggered it (see MeterCommand's doc comment for why
// these are separate entities with separate lifecycles).
app.MapGet("/api/v1/meter-commands", async (PrepaidEngineDbContext db) =>
{
    var commands = await (
        from m in db.MeterCommands
        join c in db.Consumers on m.ConsumerId equals c.Id
        join r in db.RechargeTransactions on m.RechargeTransactionId equals r.Id
        orderby m.CreatedAt descending
        select new
        {
            m.Id,
            c.AccountNumber,
            c.Name,
            m.CreditAmount,
            m.Status,
            m.RetryCount,
            m.ErrorMessage,
            m.CreatedAt,
            m.SentAt,
            m.AcknowledgedAt,
            RechargeTransactionId = r.Id,
            r.RmsReferenceId,
        })
        .ToListAsync();

    return Results.Ok(commands);
})
.WithName("ListMeterCommands")
.RequireAuthorization();

app.MapGet("/api/v1/meter-commands/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
{
    var command = await db.MeterCommands.FirstOrDefaultAsync(m => m.Id == id);
    if (command is null)
        return Results.NotFound();

    var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.Id == command.ConsumerId);
    var recharge = await db.RechargeTransactions.FirstOrDefaultAsync(r => r.Id == command.RechargeTransactionId);
    if (consumer is null || recharge is null)
    {
        return Results.Problem(
            statusCode: StatusCodes.Status500InternalServerError,
            title: "Meter command references missing data",
            detail: $"Meter command {id} references a consumer or recharge that no longer exists.");
    }

    return Results.Ok(new
    {
        command.Id,
        Consumer = new { consumer.AccountNumber, consumer.Name },
        command.CreditAmount,
        command.Status,
        command.RetryCount,
        command.ErrorMessage,
        command.CreatedAt,
        command.SentAt,
        command.AcknowledgedAt,
        Recharge = new { recharge.Id, recharge.RmsReferenceId, recharge.Amount },
    });
})
.WithName("GetMeterCommandById")
.RequireAuthorization();

// Retries a Failed/TimedOut meter command: resets it to Queued via MeterCommand.Retry()
// (incrementing RetryCount, clearing the prior error/SentAt), then dispatches it again through
// IMeterCommandClient exactly like the original attempt — never fabricates a retry result.
app.MapPost("/api/v1/meter-commands/{id:guid}/retry", async (
    Guid id,
    PrepaidEngineDbContext db,
    IMeterCommandClient meterCommandClient) =>
{
    var command = await db.MeterCommands.FirstOrDefaultAsync(m => m.Id == id);
    if (command is null)
        return Results.NotFound();

    try
    {
        command.Retry();
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }

    command.MarkSent(DateTime.UtcNow);

    var correlationId = $"retry-{command.RetryCount}-{Guid.NewGuid():N}";
    var meterResult = await meterCommandClient.SendCreditCommandAsync(
        new SendCreditCommandRequest(command.ConsumerId, command.CreditAmount, correlationId));

    switch (meterResult.Outcome)
    {
        case MeterCommandOutcome.Acknowledged:
            command.MarkAcknowledged(DateTime.UtcNow);
            break;
        case MeterCommandOutcome.Failed:
            command.MarkFailed(meterResult.Message ?? "Meter rejected the credit command.");
            RaiseException(db, OperationalExceptionSourceType.MeterCommand, command.Id, command.ConsumerId,
                $"Meter command {command.Id} failed on retry #{command.RetryCount}: {command.ErrorMessage}");
            break;
        case MeterCommandOutcome.TimedOut:
            command.MarkTimedOut();
            RaiseException(db, OperationalExceptionSourceType.MeterCommand, command.Id, command.ConsumerId,
                $"Meter command {command.Id} timed out on retry #{command.RetryCount}.");
            break;
    }

    await db.SaveChangesAsync();

    return Results.Ok(new
    {
        command.Id,
        command.Status,
        command.RetryCount,
        command.ErrorMessage,
    });
})
.WithName("RetryMeterCommand")
.RequireAuthorization();

// --- Prepaid conversion: RMS pushes a batch of postpaid->prepaid conversion requests, per -----
// "Prepaid_Integration_Requirement_Document_ProposalFromAMISP_V1.1" section 1. Each item is
// validated and applied synchronously (the spec's own response is synchronous, per-item
// Success/Fail — there is no separate approval step in the real flow), but the decision trail
// (ConversionRequest: Requested -> Approved/Rejected -> Completed) and the actual billing-mode
// change (Consumer.ConvertToPrepaid(), never a raw property set) stay two explicit calls even
// though they happen within the same request — mirroring this project's intent/execution split.
// A NET-meter consumer is always rejected (spec: "If the consumer is NET meter consumer, then
// such meter shall not be converted to prepaid"). One bad item in the batch does not fail the
// rest — each is independently validated and reported, matching the spec's per-item response.
app.MapPost("/api/v1/conversions", async (List<ConversionRequestItem>? requests, PrepaidEngineDbContext db) =>
{
    if (requests is null || requests.Count == 0)
        return Results.BadRequest(new { error = "At least one conversion request is required." });

    var responses = new List<ConversionResponseItem>();

    foreach (var item in requests)
    {
        if (string.IsNullOrWhiteSpace(item.TransactionId) || string.IsNullOrWhiteSpace(item.ConsumerNumber))
        {
            responses.Add(new ConversionResponseItem(item.TransactionId ?? string.Empty, item.ConsumerNumber ?? string.Empty,
                "Fail", "Transaction ID and Consumer Number are required."));
            continue;
        }

        var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.AccountNumber == item.ConsumerNumber);
        if (consumer is null)
        {
            responses.Add(new ConversionResponseItem(item.TransactionId, item.ConsumerNumber,
                "Fail", $"No consumer found for consumer number {item.ConsumerNumber}."));
            continue;
        }

        ConversionRequest conversion;
        try
        {
            conversion = new ConversionRequest(
                Guid.NewGuid(), consumer.Id, item.TransactionId, item.MeterSerialNumber, item.ConsumerNumber,
                item.ConsumerType, item.InitialReading, item.InitialReadingDateTime, item.ConversionDate,
                DateTime.UtcNow, item.RequestType ?? "PRE");
        }
        catch (ArgumentException ex)
        {
            responses.Add(new ConversionResponseItem(item.TransactionId, item.ConsumerNumber, "Fail", ex.Message));
            continue;
        }

        db.ConversionRequests.Add(conversion);

        if (consumer.IsNetMeter)
        {
            conversion.Reject("NET meter consumers cannot be converted to prepaid.", DateTime.UtcNow);
            Audit(db, nameof(ConversionRequest), conversion.Id.ToString(), "Rejected", "system", details: conversion.DecisionNote);
            responses.Add(new ConversionResponseItem(item.TransactionId, item.ConsumerNumber, "Fail", conversion.DecisionNote!));
            continue;
        }

        if (consumer.BillingMode == BillingMode.Prepaid)
        {
            conversion.Reject("Consumer is already billed Prepaid.", DateTime.UtcNow);
            Audit(db, nameof(ConversionRequest), conversion.Id.ToString(), "Rejected", "system", details: conversion.DecisionNote);
            responses.Add(new ConversionResponseItem(item.TransactionId, item.ConsumerNumber, "Fail", conversion.DecisionNote!));
            continue;
        }

        conversion.Approve(DateTime.UtcNow);
        conversion.Complete(DateTime.UtcNow);
        var oldMode = consumer.BillingMode.ToString();
        consumer.ConvertToPrepaid();

        Audit(db, nameof(Consumer), consumer.Id.ToString(), "BillingModeChanged", "system",
            oldValue: oldMode, newValue: consumer.BillingMode.ToString(), details: $"Conversion request {conversion.Id} completed");

        // Real SMS delivery ("D/Consumer, your CID ... is now in pre-paid mode...") is not
        // modeled by this system — this repo has no notification/SMS gateway abstraction, and
        // per the "no fake success states" rule this endpoint does not report an SMS as sent.
        // See docs/frontend-scope.md / README for this and the other spec gaps left honestly
        // unmodeled (holiday calendar, meter cumulative readings, RMS's own shadow-bill calc).

        responses.Add(new ConversionResponseItem(item.TransactionId, item.ConsumerNumber, "Success", null));
    }

    await db.SaveChangesAsync();

    return Results.Ok(responses);
})
.WithName("SubmitConversions")
.RequireAuthorization();

app.MapGet("/api/v1/conversions", async (PrepaidEngineDbContext db) =>
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
        })
        .ToListAsync();

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
    });
})
.WithName("GetConversionById")
.RequireAuthorization();

// --- Billing reconciliation: RMS-pushed signed adjustments (spec sections 7 & 8) applied to ---
// the consumer's wallet exactly like a recharge, plus the daily billing-data export AMISP needs
// to give RMS so it can compute its own shadow monthly bill and find any gap in the first place.
app.MapPost("/api/v1/consumers/{accountNumber}/reconciliation-adjustments", async (
    string accountNumber, ReconciliationAdjustmentRequest request, PrepaidEngineDbContext db) =>
{
    if (request.Amount == 0)
        return Results.BadRequest(new { error = "A reconciliation adjustment amount cannot be zero." });
    if (string.IsNullOrWhiteSpace(request.Reference))
        return Results.BadRequest(new { error = "A reference is required for a reconciliation adjustment." });

    var consumer = await db.Consumers.Include(c => c.Wallet).FirstOrDefaultAsync(c => c.AccountNumber == accountNumber);
    if (consumer is null)
        return Results.NotFound();

    var adjustment = ApplyReconciliationAdjustment(db, consumer, request.Amount, request.ReconciliationDate, request.Reference);

    Audit(db, nameof(ReconciliationAdjustment), adjustment.Id.ToString(), "Applied", "system",
        newValue: adjustment.Amount.ToString("0.00"), details: $"For {consumer.AccountNumber}: {request.Reference}");

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
.RequireAuthorization();

app.MapGet("/api/v1/reconciliation-adjustments", async (PrepaidEngineDbContext db) =>
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
        .ToListAsync();

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

// --- Operational exceptions: real, generated automatically (see RaiseException above) --------
// whenever a MeterCommand or ConnectivityCommand reaches Failed/TimedOut — never hand-entered.
app.MapGet("/api/v1/exceptions", async (PrepaidEngineDbContext db) =>
{
    var exceptions = await (
        from e in db.OperationalExceptions
        join c in db.Consumers on e.ConsumerId equals c.Id
        orderby e.CreatedAt descending
        select new
        {
            e.Id,
            c.AccountNumber,
            c.Name,
            e.SourceType,
            e.SourceId,
            e.Description,
            e.Status,
            e.ResolutionNote,
            e.CreatedAt,
            e.ResolvedAt,
        })
        .ToListAsync();

    return Results.Ok(exceptions);
})
.WithName("ListOperationalExceptions")
.RequireAuthorization();

app.MapGet("/api/v1/exceptions/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
{
    var exception = await db.OperationalExceptions.FirstOrDefaultAsync(e => e.Id == id);
    if (exception is null)
        return Results.NotFound();

    var consumer = await db.Consumers.FirstOrDefaultAsync(c => c.Id == exception.ConsumerId);
    if (consumer is null)
    {
        return Results.Problem(statusCode: StatusCodes.Status500InternalServerError,
            title: "Operational exception references missing data",
            detail: $"Operational exception {id} references a consumer that no longer exists.");
    }

    return Results.Ok(new
    {
        exception.Id,
        Consumer = new { consumer.AccountNumber, consumer.Name },
        exception.SourceType,
        exception.SourceId,
        exception.Description,
        exception.Status,
        exception.ResolutionNote,
        exception.CreatedAt,
        exception.ResolvedAt,
    });
})
.WithName("GetOperationalExceptionById")
.RequireAuthorization();

app.MapPost("/api/v1/exceptions/{id:guid}/resolve", async (Guid id, ResolutionRequest request, PrepaidEngineDbContext db) =>
{
    var exception = await db.OperationalExceptions.FirstOrDefaultAsync(e => e.Id == id);
    if (exception is null)
        return Results.NotFound();

    if (string.IsNullOrWhiteSpace(request.Note))
        return Results.BadRequest(new { error = "A resolution note is required." });

    try
    {
        exception.Resolve(request.Note, DateTime.UtcNow);
    }
    catch (InvalidOperationException ex)
    {
        return Results.Conflict(new { error = ex.Message });
    }

    Audit(db, nameof(OperationalException), exception.Id.ToString(), "Resolved", "system", details: request.Note);
    await db.SaveChangesAsync();

    return Results.Ok(new { exception.Id, exception.Status, exception.ResolutionNote, exception.ResolvedAt });
})
.WithName("ResolveOperationalException")
.RequireAuthorization();

// --- Audit entries: an immutable, append-only log — read-only, filterable by entity type and ---
// a date range (see README.md for exactly which actions append an entry).
app.MapGet("/api/v1/audit-entries", async (PrepaidEngineDbContext db, string? entityType, DateTime? from, DateTime? to) =>
{
    var query = db.AuditEntries.AsQueryable();

    if (!string.IsNullOrWhiteSpace(entityType))
        query = query.Where(a => a.EntityType == entityType);
    if (from.HasValue)
        query = query.Where(a => a.OccurredAt >= from.Value);
    if (to.HasValue)
        query = query.Where(a => a.OccurredAt <= to.Value);

    var entries = await query
        .OrderByDescending(a => a.OccurredAt)
        .Select(a => new
        {
            a.Id,
            a.EntityType,
            a.EntityId,
            a.Action,
            a.Actor,
            a.OldValue,
            a.NewValue,
            a.Details,
            a.OccurredAt,
        })
        .ToListAsync();

    return Results.Ok(entries);
})
.WithName("ListAuditEntries")
.RequireAuthorization();

// --- Tariff version history: append-only record of parameter changes, enabling a Tariff -------
// Change Report even though Tariff itself still only exposes its single current version (see
// README's Tariff Management section for why there is no tariff update endpoint yet).
app.MapPost("/api/v1/tariffs/{id:guid}/versions", async (Guid id, TariffVersionRequest request, PrepaidEngineDbContext db) =>
{
    var tariff = await db.Tariffs.FirstOrDefaultAsync(t => t.Id == id);
    if (tariff is null)
        return Results.NotFound();

    if (string.IsNullOrWhiteSpace(request.ChangeNote))
        return Results.BadRequest(new { error = "A change note is required to record a tariff version." });

    TariffVersion version;
    try
    {
        version = new TariffVersion(Guid.NewGuid(), tariff.Id, request.FieldName, request.OldValue, request.NewValue,
            request.ChangeNote, request.EffectiveDate, DateTime.UtcNow);
    }
    catch (ArgumentException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    db.TariffVersions.Add(version);
    Audit(db, nameof(Tariff), tariff.Id.ToString(), "VersionRecorded", "system",
        oldValue: request.OldValue, newValue: request.NewValue, details: $"{request.FieldName}: {request.ChangeNote}");

    await db.SaveChangesAsync();

    return Results.Ok(new { version.Id, version.TariffId, version.FieldName, version.OldValue, version.NewValue, version.EffectiveDate, version.RecordedAt });
})
.WithName("RecordTariffVersion")
.RequireAuthorization();

app.MapGet("/api/v1/tariffs/{id:guid}/versions", async (Guid id, PrepaidEngineDbContext db) =>
{
    var tariffExists = await db.Tariffs.AnyAsync(t => t.Id == id);
    if (!tariffExists)
        return Results.NotFound();

    var versions = await db.TariffVersions
        .Where(v => v.TariffId == id)
        .OrderByDescending(v => v.EffectiveDate)
        .Select(v => new { v.Id, v.FieldName, v.OldValue, v.NewValue, v.ChangeNote, v.EffectiveDate, v.RecordedAt })
        .ToListAsync();

    return Results.Ok(versions);
})
.WithName("ListTariffVersions")
.RequireAuthorization();

// --- Reports: honest aggregation endpoints over data that now genuinely exists -----------------
// Day-wise RC/DC report: ConnectivityCommand counts grouped by calendar day and command type.
app.MapGet("/api/v1/reports/day-wise-rc-dc", async (PrepaidEngineDbContext db, DateTime? from, DateTime? to) =>
{
    var query = db.ConnectivityCommands.AsQueryable();
    if (from.HasValue)
        query = query.Where(c => c.CreatedAt >= from.Value);
    if (to.HasValue)
        query = query.Where(c => c.CreatedAt <= to.Value);

    var commands = await query.ToListAsync();

    var dayWise = commands
        .GroupBy(c => c.CreatedAt.Date)
        .OrderBy(g => g.Key)
        .Select(g => new
        {
            Date = g.Key,
            DisconnectCount = g.Count(c => c.CommandType == ConnectivityCommandType.Disconnect),
            ReconnectCount = g.Count(c => c.CommandType == ConnectivityCommandType.Reconnect),
            AcknowledgedCount = g.Count(c => c.Status == ConnectivityCommandStatus.Acknowledged),
            FailedCount = g.Count(c => c.Status == ConnectivityCommandStatus.Failed),
            TimedOutCount = g.Count(c => c.Status == ConnectivityCommandStatus.TimedOut),
            TotalCount = g.Count(),
        })
        .ToList();

    return Results.Ok(dayWise);
})
.WithName("DayWiseRcDcReport")
.RequireAuthorization();

// Meter Credit Failure Report: every MeterCommand that is Failed or TimedOut.
app.MapGet("/api/v1/reports/meter-credit-failures", async (PrepaidEngineDbContext db) =>
{
    var failures = await (
        from m in db.MeterCommands
        join c in db.Consumers on m.ConsumerId equals c.Id
        where m.Status == MeterCommandStatus.Failed || m.Status == MeterCommandStatus.TimedOut
        orderby m.CreatedAt descending
        select new
        {
            m.Id,
            c.AccountNumber,
            c.Name,
            m.CreditAmount,
            m.Status,
            m.RetryCount,
            m.ErrorMessage,
            m.CreatedAt,
        })
        .ToListAsync();

    return Results.Ok(failures);
})
.WithName("MeterCreditFailureReport")
.RequireAuthorization();

// Recharge Failure Report: every RechargeTransaction that Failed.
app.MapGet("/api/v1/reports/recharge-failures", async (PrepaidEngineDbContext db) =>
{
    var failures = await (
        from r in db.RechargeTransactions
        join c in db.Consumers on r.ConsumerId equals c.Id
        where r.Status == RechargeStatus.Failed
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
        })
        .ToListAsync();

    return Results.Ok(failures);
})
.WithName("RechargeFailureReport")
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

/// <param name="Reason">Required. An auditable justification for the disconnect/reconnect — never optional metadata.</param>
/// <param name="CorrelationId">
/// Optional. Propagated to IConnectivityCommandClient for logs/telemetry; also doubles as the
/// mock client's outcome control (see MockConnectivityCommandClient's CONNFAIL-/CONNTIMEOUT-
/// markers). Defaults to a fresh id if omitted.
/// </param>
public record ConnectivityRequest(string Reason, string? CorrelationId = null);

/// <summary>One RMS-pushed conversion request, per AMISP integration requirement doc section 1.
/// Requests arrive as an array — a single consumer or a batch.</summary>
/// <param name="TransactionId">RMS's own transaction identifier, echoed back in the response.</param>
/// <param name="MeterSerialNumber">The smart meter's serial number.</param>
/// <param name="ConsumerNumber">RMS's consumer number — matched against Consumer.AccountNumber.</param>
/// <param name="ConsumerType">VIP/Hospital/School/ShoppingComplex/etc.</param>
/// <param name="InitialReading">Reading the last post-paid bill was based on (1st of the conversion month, 00:00 hrs).</param>
/// <param name="InitialReadingDateTime">Date/time of that initial reading.</param>
/// <param name="ConversionDate">The day RMS pushed this request.</param>
/// <param name="RequestType">"PRE" by default per spec.</param>
public record ConversionRequestItem(
    string TransactionId,
    string MeterSerialNumber,
    string ConsumerNumber,
    ConversionConsumerType ConsumerType,
    decimal InitialReading,
    DateTime InitialReadingDateTime,
    DateTime ConversionDate,
    string? RequestType = "PRE");

/// <summary>AMISP's synchronous per-request acknowledgement back to RMS, per spec section 1
/// ("Transaction ID, Consumer Number, Response code [Success/Fail], Response message").</summary>
public record ConversionResponseItem(string TransactionId, string ConsumerNumber, string ResponseCode, string? ResponseMessage);

/// <param name="Note">Required resolution note, mirroring ConnectivityCommand's mandatory Reason pattern.</param>
public record ResolutionRequest(string Note);

/// <summary>An RMS-pushed reconciliation gap/credit, per spec sections 7-8.</summary>
/// <param name="Amount">Signed: positive credits the wallet, negative debits it.</param>
/// <param name="ReconciliationDate">RMS's reconciliation date for this adjustment.</param>
/// <param name="Reference">Required free-text reference/reason RMS supplied.</param>
public record ReconciliationAdjustmentRequest(decimal Amount, DateTime ReconciliationDate, string Reference);

/// <param name="FieldName">The Tariff property that changed (e.g. "FixedChargePerUnitPerMonth").</param>
/// <param name="OldValue">The prior value, as a string (this is a change log, not a typed diff).</param>
/// <param name="NewValue">The new value, as a string.</param>
/// <param name="ChangeNote">Required — why the change was made.</param>
/// <param name="EffectiveDate">When the new value takes effect.</param>
public record TariffVersionRequest(string FieldName, string OldValue, string NewValue, string ChangeNote, DateTime EffectiveDate);

// Exposed so WebApplicationFactory-based integration tests can bootstrap this Api project.
public partial class Program { }
