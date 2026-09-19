using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Auth;
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
using PrepaidEngine.Application.Sla;
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

// TODO(MDMS/HES integration): swap for a real adapter that actually carries a payment-mode-change
// command down the MDMS -> HES -> Meter chain; keep MockPaymentModeChangeClient registered for
// local dev / tests until then.
builder.Services.AddSingleton<IPaymentModeChangeClient, MockPaymentModeChangeClient>();

// Central emergency-credit disconnect/reconnect policy — see IEmergencyCreditGuard's doc comment.
// Scoped since it holds a scoped PrepaidEngineDbContext.
builder.Services.AddScoped<IEmergencyCreditGuard, EmergencyCreditGuard>();

// The DLP billing pipeline service — see IBillingEngineService's doc comment. Scoped (not
// singleton) since it holds a scoped PrepaidEngineDbContext.
builder.Services.AddScoped<IBillingEngineService, BillingEngineService>();

// BP/LS/IP/Events ingestion + cross-source energy validation — see IMeterDataIngestionService's
// doc comment. Never bills anything; DLP billing stays entirely in IBillingEngineService above.
builder.Services.Configure<EnergyValidationOptions>(builder.Configuration.GetSection(EnergyValidationOptions.SectionName));
builder.Services.AddScoped<IMeterDataIngestionService, MeterDataIngestionService>();

// Real SLA performance for DLP ingestion/billing/recharge/meter-credit/RC-DC — see
// ISlaMonitoringService's doc comment. Targets configurable via appsettings.json.
builder.Services.Configure<SlaMonitoringOptions>(builder.Configuration.GetSection(SlaMonitoringOptions.SectionName));
builder.Services.AddScoped<ISlaMonitoringService, SlaMonitoringService>();

// Local/demo background processing for the daily billing cycle — see the worker's own doc
// comment for why this is intentionally simple.
builder.Services.AddHostedService<PrepaidEngine.Api.Billing.BillingProcessingWorker>();

// Basic auth for the demo endpoints only — a stop-gap, not a substitute for real
// authentication before any shared/production exposure (see docs/assumptions-and-security.md).
builder.Services.AddAuthentication("Basic")
    .AddScheme<BasicAuthenticationSchemeOptions, BasicAuthenticationHandler>("Basic", null);

// Two-role authorization for the tariff-governance workflow (see UserRole's doc comment) — IT
// drafts/edits/submits, Utility reviews/approves/rejects. Every pre-existing endpoint keeps using
// plain .RequireAuthorization() (no role requirement), so this is additive, not a breaking change.
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ITRole", policy => policy.RequireRole(nameof(UserRole.IT)));
    options.AddPolicy("UtilityRole", policy => policy.RequireRole(nameof(UserRole.Utility)));
});

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
    // Explicitly track the new ledger entry as Added, same as the recharge endpoint does and for
    // the same reason (see its comment): the wallet was loaded from the DB (already tracked, not
    // part of a brand-new graph), and EF's change detection does not reliably infer "newly added"
    // for an entity appended to an already-tracked entity's backing-field collection — it can
    // mis-detect it as Modified and emit a bogus UPDATE for a row that doesn't exist yet.
    var walletTransaction = amount > 0
        ? consumer.Wallet.Credit(amount, WalletTransactionType.Reconciliation, reference)
        : consumer.Wallet.Debit(-amount, WalletTransactionType.Reconciliation, reference);
    db.WalletTransactions.Add(walletTransaction);

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
    await DbSeeder.SeedExtraConsumersAsync(db);

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

// Server-side searchable, keyset-paginated consumer list for the Consumers screen (the
// unpaginated ListConsumers above stays for the dashboard's aggregates). Identifier fields match
// by prefix so they can use indexes; name matches by substring. Keyset on AccountNumber (unique)
// avoids deep OFFSET. "Low balance" here means balance below the emergency-credit limit, the
// same definition the dashboard uses; a configurable threshold does not exist yet.
app.MapGet("/api/v1/consumers/search", async (
    string? q, PrepaidEngine.Domain.Enums.ConnectionStatus? status, bool? lowBalance,
    string? after, int? pageSize, PrepaidEngineDbContext db) =>
{
    var size = Math.Clamp(pageSize ?? 25, 1, 100);

    var query = db.Consumers.AsNoTracking().AsQueryable();

    if (!string.IsNullOrWhiteSpace(q))
    {
        var term = q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var prefix = term + "%";
        var contains = "%" + term + "%";
        query = query.Where(c =>
            EF.Functions.ILike(c.AccountNumber, prefix) ||
            EF.Functions.ILike(c.Meter.MeterNumber, prefix) ||
            (c.MobileNumber != null && EF.Functions.ILike(c.MobileNumber, prefix)) ||
            EF.Functions.ILike(c.Name, contains));
    }
    if (status.HasValue)
        query = query.Where(c => c.ConnectionStatus == status.Value);
    if (lowBalance == true)
        query = query.Where(c => c.Wallet.Balance < c.Wallet.EmergencyCreditLimit);

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
            LowBalance = c.Wallet.Balance < c.Wallet.EmergencyCreditLimit,
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
        consumer.Id,
        consumer.AccountNumber,
        consumer.Name,
        consumer.MobileNumber,
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
app.MapGet("/api/v1/connectivity-commands", async (string? accountNumber, PrepaidEngineDbContext db) =>
{
    // accountNumber narrows the list to one consumer (capped) for the Consumer Detail tab.
    var commands = await (
        from c in db.ConnectivityCommands
        join consumer in db.Consumers on c.ConsumerId equals consumer.Id
        where accountNumber == null || consumer.AccountNumber == accountNumber
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
        .Take(accountNumber == null ? int.MaxValue : 50)
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

// Server-side searchable, keyset-paginated bill list for the Billing screen (the unpaginated
// ListBills above stays for any caller that needs every row). Newest first; the cursor is
// "<GeneratedAt ticks>_<Id>". Identifier fields match by prefix, tariff and consumer name by substring.
app.MapGet("/api/v1/bills/search", async (
    string? q, BillStatus? status, DateTime? from, DateTime? to, string? after, int? pageSize,
    PrepaidEngineDbContext db) =>
{
    var size = Math.Clamp(pageSize ?? 25, 1, 100);

    var query =
        from b in db.Bills.AsNoTracking()
        join c in db.Consumers.AsNoTracking() on b.ConsumerId equals c.Id
        join t in db.Tariffs.AsNoTracking() on b.TariffId equals t.Id
        select new { b, c, t };

    if (!string.IsNullOrWhiteSpace(q))
    {
        var term = q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var prefix = term + "%";
        var contains = "%" + term + "%";
        query = query.Where(x =>
            EF.Functions.ILike(x.c.AccountNumber, prefix) ||
            EF.Functions.ILike(x.c.Name, contains) ||
            EF.Functions.ILike(x.t.Name, contains));
    }
    if (status.HasValue)
        query = query.Where(x => x.b.Status == status.Value);
    if (from.HasValue)
        query = query.Where(x => x.b.GeneratedAt >= DateTime.SpecifyKind(from.Value, DateTimeKind.Utc));
    if (to.HasValue)
        query = query.Where(x => x.b.GeneratedAt < DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc));

    var totalCount = await query.CountAsync();

    if (!string.IsNullOrEmpty(after))
    {
        var parts = after.Split('_', 2);
        if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out var afterId))
            return Results.BadRequest(new { error = "Invalid cursor." });
        var afterAt = new DateTime(ticks, DateTimeKind.Utc);
        query = query.Where(x => x.b.GeneratedAt < afterAt || (x.b.GeneratedAt == afterAt && x.b.Id.CompareTo(afterId) < 0));
    }

    var rows = await query
        .OrderByDescending(x => x.b.GeneratedAt).ThenByDescending(x => x.b.Id)
        .Take(size + 1)
        .Select(x => new
        {
            x.b.Id,
            x.c.AccountNumber,
            x.c.Name,
            Category = x.t.Category,
            TariffName = x.t.Name,
            TariffId = x.t.Id,
            EnergyChargeNet = x.b.EnergyChargeGross - x.b.PrepaidRebateAmount,
            x.b.FixedCharge,
            x.b.ElectricityDutyAmount,
            x.b.FppasAmount,
            x.b.Amount,
            x.b.AmountPaid,
            x.b.Status,
            x.b.GeneratedAt,
        })
        .ToListAsync();

    var hasMore = rows.Count > size;
    var items = hasMore ? rows.Take(size).ToList() : rows;
    var nextCursor = hasMore ? $"{items[^1].GeneratedAt.Ticks}_{items[^1].Id}" : null;
    return Results.Ok(new { items, nextCursor, totalCount });
})
.WithName("SearchBills")
.RequireAuthorization();

// Database-side aggregates for the Billing KPI strip.
app.MapGet("/api/v1/bills/summary", async (PrepaidEngineDbContext db) =>
{
    var byStatus = await db.Bills.AsNoTracking()
        .GroupBy(b => b.Status)
        .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(b => b.Amount), Paid = g.Sum(b => b.AmountPaid) })
        .ToListAsync();

    int Count(BillStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
    var total = byStatus.Sum(x => x.Count);
    var billed = byStatus.Sum(x => x.Amount);
    var paid = byStatus.Sum(x => x.Paid);

    return Results.Ok(new
    {
        Total = total,
        Paid = Count(BillStatus.Paid),
        PartiallyPaid = Count(BillStatus.PartiallyPaid),
        Generated = Count(BillStatus.Generated),
        Overdue = Count(BillStatus.Overdue),
        Cancelled = Count(BillStatus.Cancelled),
        TotalBilled = billed,
        TotalSettled = paid,
        Outstanding = billed - paid,
    });
})
.WithName("GetBillSummary")
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

    // Energy charge split by slab, computed here (never in the browser) from the exact tariff row the
    // bill references. The tariff row is immutable, so this reproduces the historical calculation.
    // If the slab sum does not match the stored gross charge (e.g. a ToD tariff), say so instead of
    // presenting a breakdown that does not explain the bill.
    var slabBreakdown = tariff.Slabs
        .OrderBy(sl => sl.FromKwh)
        .Select(sl =>
        {
            var upper = sl.UpToKwh ?? reading.ConsumptionKwh;
            var kwhInSlab = Math.Max(0m, Math.Min(reading.ConsumptionKwh, upper) - sl.FromKwh);
            return new { sl.FromKwh, sl.UpToKwh, sl.RatePerKwh, KwhInSlab = kwhInSlab, Charge = kwhInSlab * sl.RatePerKwh };
        })
        .ToList();
    var slabBreakdownReconciles = slabBreakdown.Count > 0
        && Math.Abs(slabBreakdown.Sum(x => x.Charge) - bill.EnergyChargeGross) <= 0.01m;

    return Results.Ok(new
    {
        bill.Id,
        Consumer = new { consumer.AccountNumber, consumer.Name },
        Tariff = new { tariff.Id, tariff.Name, tariff.Category, tariff.FixedChargePerUnitPerMonth, tariff.PrepaidEnergyRebatePercent, tariff.Status },
        Reading = new { reading.ConsumptionKwh, reading.PeriodStart, reading.PeriodEnd },
        SlabBreakdown = slabBreakdown,
        SlabBreakdownReconciles = slabBreakdownReconciles,
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

// Tells the caller who they are and which of the two tariff-governance roles (IT/Utility) their
// credential carries, purely so the frontend can decide which actions to render — the backend's
// own RequireAuthorization("ITRole"/"UtilityRole") policies are the actual security boundary and
// are enforced independently of what this endpoint returns.
app.MapGet("/api/v1/auth/whoami", (ClaimsPrincipal user) =>
    Results.Ok(new
    {
        Username = user.Identity?.Name ?? "unknown",
        Role = user.FindFirst(ClaimTypes.Role)?.Value,
    }))
.WithName("WhoAmI")
.RequireAuthorization();

// Tariff Management read endpoints — the real Tariff/TariffSlab/TouPeriod configuration this
// engine actually bills against (see docs/tariff-validation-report.md for sourcing). A Tariff
// row itself is still never edited in place — see TariffChangeRequest's doc comment for the
// governance workflow that now exists to create a new one instead.
app.MapGet("/api/v1/tariffs", async (PrepaidEngine.Domain.Enums.TariffLifecycleStatus? status, PrepaidEngineDbContext db) =>
{
    var query = db.Tariffs.AsQueryable();
    if (status.HasValue) query = query.Where(t => t.Status == status.Value);

    var tariffs = await query
        .Select(t => new
        {
            t.Id,
            t.Name,
            t.Category,
            t.Status,
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
        tariff.Status,
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

// --- Tariff governance (Phase 1: Tariff Governance + MDM Recharge Command Integration) --------
// See TariffChangeRequest's own doc comment for the full workflow. IT (CREATE/EDIT/DRAFT/SUBMIT)
// and Utility (APPROVE/REJECT) are role-gated at the endpoint level — the primary defense against
// self-approval — with a same-actor check inside TariffChangeRequest.Approve() as defense in depth.

app.MapPost("/api/v1/tariff-change-requests", async (CreateTariffChangeRequestBody request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
{
    var actor = user.Identity?.Name ?? "unknown";

    if (request.SupersedesTariffId.HasValue)
    {
        var superseded = await db.Tariffs.FirstOrDefaultAsync(t => t.Id == request.SupersedesTariffId.Value);
        if (superseded is null)
            return Results.BadRequest(new { error = $"No tariff found with id '{request.SupersedesTariffId}' to revise." });
        if (superseded.Status == PrepaidEngine.Domain.Enums.TariffLifecycleStatus.Retired)
            return Results.BadRequest(new { error = "Cannot revise a tariff that has already been retired." });

        var conflictingPending = await db.TariffChangeRequests.AnyAsync(r =>
            r.SupersedesTariffId == request.SupersedesTariffId.Value &&
            (r.Status == TariffChangeRequestStatus.PendingApproval || r.Status == TariffChangeRequestStatus.Scheduled));
        if (conflictingPending)
        {
            return Results.Conflict(new
            {
                error = "This tariff already has a pending-approval or scheduled change request. Resolve it before creating another.",
            });
        }
    }

    TariffChangeRequest changeRequest;
    try
    {
        changeRequest = new TariffChangeRequest(
            Guid.NewGuid(), request.SupersedesTariffId, request.ProposedName, request.ProposedCategory,
            request.ProposedSlabs.Select(s => new TariffSlab(s.FromKwh, s.UpToKwh, s.RatePerKwh)),
            request.ProposedFixedChargePerUnitPerMonth, request.ProposedPrepaidEnergyRebatePercent, request.ProposedEmergencyCreditLimit,
            actor, DateTime.UtcNow,
            request.ProposedMinVendAmountSinglePhase, request.ProposedMaxVendAmountSinglePhase,
            request.ProposedMinVendAmountThreePhase, request.ProposedMaxVendAmountThreePhase,
            request.ProposedTouPeriods?.Select(p => new TouPeriod(p.Label, TimeSpan.Parse(p.StartTime), TimeSpan.Parse(p.EndTime), p.RatePerKvah)));
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    db.TariffChangeRequests.Add(changeRequest);
    Audit(db, nameof(TariffChangeRequest), changeRequest.Id.ToString(), "TARIFF_CREATED", actor,
        details: $"Proposed '{changeRequest.ProposedName}'" + (request.SupersedesTariffId.HasValue ? $" revising tariff {request.SupersedesTariffId}." : " as a new tariff."));

    await db.SaveChangesAsync();

    return Results.Ok(new { changeRequest.Id, changeRequest.Status });
})
.WithName("CreateTariffChangeRequest")
.RequireAuthorization("ITRole");

app.MapPut("/api/v1/tariff-change-requests/{id:guid}/draft", async (Guid id, UpdateTariffChangeRequestBody request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
{
    var actor = user.Identity?.Name ?? "unknown";
    var changeRequest = await db.TariffChangeRequests
        .Include(r => r.ProposedSlabs).Include(r => r.ProposedTouPeriods)
        .FirstOrDefaultAsync(r => r.Id == id);
    if (changeRequest is null)
        return Results.NotFound();

    try
    {
        changeRequest.UpdateProposal(
            request.ProposedName, request.ProposedCategory,
            request.ProposedSlabs.Select(s => new TariffSlab(s.FromKwh, s.UpToKwh, s.RatePerKwh)),
            request.ProposedFixedChargePerUnitPerMonth, request.ProposedPrepaidEnergyRebatePercent, request.ProposedEmergencyCreditLimit,
            request.ProposedMinVendAmountSinglePhase, request.ProposedMaxVendAmountSinglePhase,
            request.ProposedMinVendAmountThreePhase, request.ProposedMaxVendAmountThreePhase,
            request.ProposedTouPeriods?.Select(p => new TouPeriod(p.Label, TimeSpan.Parse(p.StartTime), TimeSpan.Parse(p.EndTime), p.RatePerKvah)));
    }
    catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    Audit(db, nameof(TariffChangeRequest), changeRequest.Id.ToString(), "DRAFT_SAVED", actor);
    await db.SaveChangesAsync();

    return Results.Ok(new { changeRequest.Id, changeRequest.Status });
})
.WithName("UpdateTariffChangeRequestDraft")
.RequireAuthorization("ITRole");

app.MapPost("/api/v1/tariff-change-requests/{id:guid}/submit", async (Guid id, SubmitTariffChangeRequestBody request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
{
    var actor = user.Identity?.Name ?? "unknown";
    var changeRequest = await db.TariffChangeRequests
        .Include(r => r.ProposedSlabs).Include(r => r.ProposedTouPeriods)
        .FirstOrDefaultAsync(r => r.Id == id);
    if (changeRequest is null)
        return Results.NotFound();

    var validationErrors = changeRequest.ValidateForSubmission();
    if (validationErrors.Count > 0)
        return Results.BadRequest(new { errors = validationErrors });

    try
    {
        changeRequest.Submit(actor, request.ChangeReason, DateTime.UtcNow);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    Audit(db, nameof(TariffChangeRequest), changeRequest.Id.ToString(), "SUBMITTED", actor, details: request.ChangeReason);
    await db.SaveChangesAsync();

    return Results.Ok(new { changeRequest.Id, changeRequest.Status });
})
.WithName("SubmitTariffChangeRequest")
.RequireAuthorization("ITRole");

app.MapGet("/api/v1/tariff-change-requests", async (TariffChangeRequestStatus? status, PrepaidEngineDbContext db) =>
{
    var query = db.TariffChangeRequests.AsQueryable();
    if (status.HasValue) query = query.Where(r => r.Status == status.Value);

    var requests = await query
        .OrderByDescending(r => r.CreatedAt)
        .Select(r => new
        {
            r.Id, r.SupersedesTariffId, r.ResultingTariffId, r.ProposedName, r.ProposedCategory, r.Status,
            r.CreatedBy, r.CreatedAt, r.ChangeReason, r.SubmittedBy, r.SubmittedAt,
            r.ApprovedBy, r.ApprovedAt, r.CommencementDate, r.RejectedBy, r.RejectedAt, r.RejectionReason, r.ActivatedAt,
        })
        .Take(500)
        .ToListAsync();

    return Results.Ok(requests);
})
.WithName("ListTariffChangeRequests")
.RequireAuthorization();

app.MapGet("/api/v1/tariff-change-requests/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
{
    var changeRequest = await db.TariffChangeRequests
        .Include(r => r.ProposedSlabs).Include(r => r.ProposedTouPeriods)
        .FirstOrDefaultAsync(r => r.Id == id);
    if (changeRequest is null)
        return Results.NotFound();

    Tariff? currentTariff = changeRequest.SupersedesTariffId.HasValue
        ? await db.Tariffs.Include(t => t.Slabs).Include(t => t.TouPeriods).FirstOrDefaultAsync(t => t.Id == changeRequest.SupersedesTariffId.Value)
        : null;

    return Results.Ok(new
    {
        changeRequest.Id,
        changeRequest.SupersedesTariffId,
        changeRequest.ResultingTariffId,
        changeRequest.Status,
        Proposed = new
        {
            changeRequest.ProposedName,
            changeRequest.ProposedCategory,
            changeRequest.ProposedFixedChargePerUnitPerMonth,
            changeRequest.ProposedPrepaidEnergyRebatePercent,
            changeRequest.ProposedEmergencyCreditLimit,
            changeRequest.ProposedMinVendAmountSinglePhase,
            changeRequest.ProposedMaxVendAmountSinglePhase,
            changeRequest.ProposedMinVendAmountThreePhase,
            changeRequest.ProposedMaxVendAmountThreePhase,
            Slabs = changeRequest.ProposedSlabs.OrderBy(s => s.FromKwh).Select(s => new { s.FromKwh, s.UpToKwh, s.RatePerKwh }),
            TouPeriods = changeRequest.ProposedTouPeriods.Select(p => new { p.Label, StartTime = p.StartTime.ToString(), EndTime = p.EndTime.ToString(), p.RatePerKvah }),
        },
        Current = currentTariff == null ? null : new
        {
            currentTariff.Name,
            currentTariff.Category,
            currentTariff.FixedChargePerUnitPerMonth,
            currentTariff.PrepaidEnergyRebatePercent,
            currentTariff.EmergencyCreditLimit,
            currentTariff.MinVendAmountSinglePhase,
            currentTariff.MaxVendAmountSinglePhase,
            currentTariff.MinVendAmountThreePhase,
            currentTariff.MaxVendAmountThreePhase,
            Slabs = currentTariff.Slabs.OrderBy(s => s.FromKwh).Select(s => new { s.FromKwh, s.UpToKwh, s.RatePerKwh }),
            TouPeriods = currentTariff.TouPeriods.Select(p => new { p.Label, StartTime = p.StartTime.ToString(), EndTime = p.EndTime.ToString(), p.RatePerKvah }),
        },
        changeRequest.CreatedBy,
        changeRequest.CreatedAt,
        changeRequest.ChangeReason,
        changeRequest.SubmittedBy,
        changeRequest.SubmittedAt,
        changeRequest.ApprovedBy,
        changeRequest.ApprovedAt,
        changeRequest.CommencementDate,
        changeRequest.RejectedBy,
        changeRequest.RejectedAt,
        changeRequest.RejectionReason,
        changeRequest.ActivatedAt,
    });
})
.WithName("GetTariffChangeRequestById")
.RequireAuthorization();

app.MapPost("/api/v1/tariff-change-requests/{id:guid}/approve", async (Guid id, ApproveTariffChangeRequestBody request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
{
    var actor = user.Identity?.Name ?? "unknown";
    var changeRequest = await db.TariffChangeRequests.FirstOrDefaultAsync(r => r.Id == id);
    if (changeRequest is null)
        return Results.NotFound();

    // Postgres timestamptz columns require Kind=Utc; a date-only value from JSON model
    // binding comes back as Kind=Unspecified, which Npgsql rejects at save time.
    var commencementDate = DateTime.SpecifyKind(request.CommencementDate.Date, DateTimeKind.Utc);

    try
    {
        changeRequest.Approve(actor, commencementDate, DateTime.UtcNow);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    Audit(db, nameof(TariffChangeRequest), changeRequest.Id.ToString(), "APPROVED", actor,
        details: $"Commencement {commencementDate:d}");
    await db.SaveChangesAsync();

    return Results.Ok(new { changeRequest.Id, changeRequest.Status, changeRequest.CommencementDate });
})
.WithName("ApproveTariffChangeRequest")
.RequireAuthorization("UtilityRole");

app.MapPost("/api/v1/tariff-change-requests/{id:guid}/reject", async (Guid id, RejectTariffChangeRequestBody request, ClaimsPrincipal user, PrepaidEngineDbContext db) =>
{
    var actor = user.Identity?.Name ?? "unknown";
    var changeRequest = await db.TariffChangeRequests.FirstOrDefaultAsync(r => r.Id == id);
    if (changeRequest is null)
        return Results.NotFound();

    try
    {
        changeRequest.Reject(actor, request.RejectionReason, DateTime.UtcNow);
    }
    catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    Audit(db, nameof(TariffChangeRequest), changeRequest.Id.ToString(), "REJECTED", actor, details: request.RejectionReason);
    await db.SaveChangesAsync();

    return Results.Ok(new { changeRequest.Id, changeRequest.Status });
})
.WithName("RejectTariffChangeRequest")
.RequireAuthorization("UtilityRole");

// Activates every Scheduled request whose commencement date has arrived — materializes the new
// immutable Tariff row and retires the superseded one (if any). Idempotent/atomic per request via
// the same Status guard TariffChangeRequest.Activate already enforces (a request can only ever be
// Activated once). Exposed as a real endpoint (rather than only a background job) so the demo can
// trigger it deterministically instead of waiting on wall-clock time.
app.MapPost("/api/v1/tariff-change-requests/activate-due", async (PrepaidEngineDbContext db) =>
{
    var today = DateTime.UtcNow;
    var due = await db.TariffChangeRequests
        .Where(r => r.Status == TariffChangeRequestStatus.Scheduled && r.CommencementDate!.Value.Date <= today.Date)
        .ToListAsync();

    var activated = new List<object>();
    foreach (var changeRequest in due)
    {
        // AsNoTracking is required here: EF Core cannot track an owned-entity collection queried
        // on its own without its owner also present in the same result set.
        var proposedSlabs = await db.Entry(changeRequest).Collection(r => r.ProposedSlabs).Query().AsNoTracking().ToListAsync();
        var proposedTou = await db.Entry(changeRequest).Collection(r => r.ProposedTouPeriods).Query().AsNoTracking().ToListAsync();

        var newTariff = new Tariff(
            Guid.NewGuid(), changeRequest.ProposedName, changeRequest.ProposedCategory,
            proposedSlabs.Select(s => new TariffSlab(s.FromKwh, s.UpToKwh, s.RatePerKwh)),
            changeRequest.ProposedFixedChargePerUnitPerMonth, changeRequest.ProposedPrepaidEnergyRebatePercent, changeRequest.ProposedEmergencyCreditLimit,
            changeRequest.ProposedMinVendAmountSinglePhase, changeRequest.ProposedMaxVendAmountSinglePhase,
            changeRequest.ProposedMinVendAmountThreePhase, changeRequest.ProposedMaxVendAmountThreePhase,
            proposedTou.Select(p => new TouPeriod(p.Label, p.StartTime, p.EndTime, p.RatePerKvah)));
        db.Tariffs.Add(newTariff);

        if (changeRequest.SupersedesTariffId.HasValue)
        {
            var superseded = await db.Tariffs.FirstOrDefaultAsync(t => t.Id == changeRequest.SupersedesTariffId.Value);
            superseded?.Retire();
        }

        changeRequest.Activate(newTariff.Id, today);
        Audit(db, nameof(TariffChangeRequest), changeRequest.Id.ToString(), "ACTIVATED", "system",
            details: $"New tariff {newTariff.Id} ('{newTariff.Name}') is now active.");
        if (changeRequest.SupersedesTariffId.HasValue)
            Audit(db, nameof(Tariff), changeRequest.SupersedesTariffId.Value.ToString(), "RETIRED", "system");

        activated.Add(new { changeRequest.Id, NewTariffId = newTariff.Id, newTariff.Name });
    }

    if (activated.Count > 0)
        await db.SaveChangesAsync();

    return Results.Ok(activated);
})
.WithName("ActivateDueTariffChangeRequests")
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
app.MapGet("/api/v1/recharges", async (string? accountNumber, PrepaidEngineDbContext db) =>
{
    // Left join to MeterCommands: a recharge that never reached RMS Success (or hasn't been
    // dispatched to the meter yet) legitimately has no command row — that must render as "no
    // meter command exists", not be dropped from the list or crash the query.
    var recharges = await (
        from r in db.RechargeTransactions
        join c in db.Consumers on r.ConsumerId equals c.Id
        join mcOuter in db.MeterCommands on r.Id equals mcOuter.RechargeTransactionId into mcGroup
        from mc in mcGroup.DefaultIfEmpty()
        where accountNumber == null || c.AccountNumber == accountNumber
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
        .Take(accountNumber == null ? int.MaxValue : 50)
        .ToListAsync();

    return Results.Ok(recharges);
})
.WithName("ListRecharges")
.RequireAuthorization();

// Server-side searchable, keyset-paginated recharge list for Recharge Operations (the unpaginated
// ListRecharges above remains for per-consumer views). Ordered newest first; the cursor is
// "<InitiatedAt ticks>_<Id>" so ties on timestamp still page deterministically. Payment status and
// meter-credit status are separate filters on purpose: RMS confirming payment never implies the
// meter was credited. meterCredit=None matches recharges with no meter command at all.
app.MapGet("/api/v1/recharges/search", async (
    string? q, RechargeStatus? paymentStatus, string? meterCredit, string? after, int? pageSize,
    PrepaidEngineDbContext db) =>
{
    var size = Math.Clamp(pageSize ?? 25, 1, 100);

    var query =
        from r in db.RechargeTransactions.AsNoTracking()
        join c in db.Consumers.AsNoTracking() on r.ConsumerId equals c.Id
        join mcOuter in db.MeterCommands.AsNoTracking() on r.Id equals mcOuter.RechargeTransactionId into mcGroup
        from mc in mcGroup.DefaultIfEmpty()
        select new { r, c, mc };

    if (!string.IsNullOrWhiteSpace(q))
    {
        var term = q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var prefix = term + "%";
        var contains = "%" + term + "%";
        query = query.Where(x =>
            EF.Functions.ILike(x.c.AccountNumber, prefix) ||
            EF.Functions.ILike(x.r.RmsReferenceId, prefix) ||
            EF.Functions.ILike(x.c.Name, contains));
    }
    if (paymentStatus.HasValue)
        query = query.Where(x => x.r.Status == paymentStatus.Value);
    if (!string.IsNullOrWhiteSpace(meterCredit))
    {
        if (string.Equals(meterCredit, "None", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.mc == null);
        else if (string.Equals(meterCredit, "FailedOrTimedOut", StringComparison.OrdinalIgnoreCase))
            query = query.Where(x => x.mc != null && (x.mc.Status == MeterCommandStatus.Failed || x.mc.Status == MeterCommandStatus.TimedOut));
        else if (Enum.TryParse<MeterCommandStatus>(meterCredit, true, out var mcs))
            query = query.Where(x => x.mc != null && x.mc.Status == mcs);
        else
            return Results.BadRequest(new { error = $"Unknown meterCredit value '{meterCredit}'." });
    }

    var totalCount = await query.CountAsync();

    if (!string.IsNullOrEmpty(after))
    {
        var parts = after.Split('_', 2);
        if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out var afterId))
            return Results.BadRequest(new { error = "Invalid cursor." });
        var afterAt = new DateTime(ticks, DateTimeKind.Utc);
        query = query.Where(x => x.r.InitiatedAt < afterAt || (x.r.InitiatedAt == afterAt && x.r.Id.CompareTo(afterId) < 0));
    }

    var rows = await query
        .OrderByDescending(x => x.r.InitiatedAt).ThenByDescending(x => x.r.Id)
        .Take(size + 1)
        .Select(x => new
        {
            x.r.Id,
            x.c.AccountNumber,
            x.c.Name,
            MeterNumber = x.c.Meter.MeterNumber,
            x.r.Amount,
            x.r.RmsReferenceId,
            x.r.Status,
            x.r.InitiatedAt,
            x.r.CompletedAt,
            MeterCommandStatus = x.mc == null ? (MeterCommandStatus?)null : x.mc.Status,
        })
        .ToListAsync();

    var hasMore = rows.Count > size;
    var items = hasMore ? rows.Take(size).ToList() : rows;
    var nextCursor = hasMore ? $"{items[^1].InitiatedAt.Ticks}_{items[^1].Id}" : null;
    return Results.Ok(new { items, nextCursor, totalCount });
})
.WithName("SearchRecharges")
.RequireAuthorization();

// Aggregate counts for the Recharge Operations KPI strip, computed in the database so the
// browser never has to load every recharge to render a KPI.
app.MapGet("/api/v1/recharges/summary", async (PrepaidEngineDbContext db) =>
{
    var byStatus = await db.RechargeTransactions.AsNoTracking()
        .GroupBy(r => r.Status)
        .Select(g => new { Status = g.Key, Count = g.Count(), Amount = g.Sum(r => r.Amount) })
        .ToListAsync();
    var byCredit = await db.MeterCommands.AsNoTracking()
        .GroupBy(m => m.Status)
        .Select(g => new { Status = g.Key, Count = g.Count() })
        .ToListAsync();

    int Payments(RechargeStatus s) => byStatus.FirstOrDefault(x => x.Status == s)?.Count ?? 0;
    int Credits(MeterCommandStatus s) => byCredit.FirstOrDefault(x => x.Status == s)?.Count ?? 0;

    return Results.Ok(new
    {
        Total = byStatus.Sum(x => x.Count),
        PaymentSuccess = Payments(RechargeStatus.Success),
        PaymentFailed = Payments(RechargeStatus.Failed),
        PaymentPending = Payments(RechargeStatus.Initiated),
        PaymentReversed = Payments(RechargeStatus.Reversed),
        AmountSucceeded = byStatus.FirstOrDefault(x => x.Status == RechargeStatus.Success)?.Amount ?? 0m,
        MeterCredited = Credits(MeterCommandStatus.Acknowledged),
        MeterCreditAwaiting = Credits(MeterCommandStatus.Queued) + Credits(MeterCommandStatus.Sent),
        MeterCreditFailed = Credits(MeterCommandStatus.Failed) + Credits(MeterCommandStatus.TimedOut),
    });
})
.WithName("GetRechargeSummary")
.RequireAuthorization();

app.MapGet("/api/v1/recharges/{id:guid}", async (Guid id, PrepaidEngineDbContext db) =>
{
    var recharge = await db.RechargeTransactions.FirstOrDefaultAsync(r => r.Id == id);
    if (recharge is null)
        return Results.NotFound();

    var consumer = await db.Consumers.Include(c => c.Wallet).Include(c => c.Meter).FirstOrDefaultAsync(c => c.Id == recharge.ConsumerId);
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
        Consumer = new { consumer.AccountNumber, consumer.Name, consumer.Meter.MeterNumber },
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
            meterCommand.ExternalCommandId,
            meterCommand.ResponseCode,
            meterCommand.ResponseMessage,
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
    IMeterCommandClient meterCommandClient,
    IEmergencyCreditGuard emergencyCreditGuard) =>
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

            var meterResult = await meterCommandClient.SendCreditCommandAsync(
                new SendCreditCommandRequest(consumer.Id, request.Amount, request.IdempotencyKey));
            meterCommand.MarkSent(DateTime.UtcNow, meterResult.ExternalCommandId);

            switch (meterResult.Outcome)
            {
                case MeterCommandOutcome.Acknowledged:
                    meterCommand.MarkAcknowledged(DateTime.UtcNow, meterResult.ResponseCode, meterResult.Message);
                    break;
                case MeterCommandOutcome.Failed:
                    meterCommand.MarkFailed(meterResult.Message ?? "Meter rejected the credit command.", meterResult.ResponseCode);
                    RaiseException(db, OperationalExceptionSourceType.MeterCommand, meterCommand.Id, consumer.Id,
                        $"Meter command {meterCommand.Id} failed: {meterCommand.ErrorMessage}");
                    break;
                case MeterCommandOutcome.TimedOut:
                    meterCommand.MarkTimedOut();
                    RaiseException(db, OperationalExceptionSourceType.MeterCommand, meterCommand.Id, consumer.Id,
                        $"Meter command {meterCommand.Id} timed out waiting for meter acknowledgement.");
                    break;
            }

            // The wallet was credited above regardless of the meter command's own outcome — check
            // for an auto-reconnect independently of whether the meter command itself succeeded.
            await emergencyCreditGuard.EvaluateAsync(consumer);

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
        command.ExternalCommandId,
        command.ResponseCode,
        command.ResponseMessage,
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

    var correlationId = $"retry-{command.RetryCount}-{Guid.NewGuid():N}";
    var meterResult = await meterCommandClient.SendCreditCommandAsync(
        new SendCreditCommandRequest(command.ConsumerId, command.CreditAmount, correlationId));
    command.MarkSent(DateTime.UtcNow, meterResult.ExternalCommandId);

    switch (meterResult.Outcome)
    {
        case MeterCommandOutcome.Acknowledged:
            command.MarkAcknowledged(DateTime.UtcNow, meterResult.ResponseCode, meterResult.Message);
            break;
        case MeterCommandOutcome.Failed:
            command.MarkFailed(meterResult.Message ?? "Meter rejected the credit command.", meterResult.ResponseCode);
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
.RequireAuthorization();

app.MapGet("/api/v1/conversions/reverse", async (PrepaidEngineDbContext db) =>
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
        .ToListAsync();

    return Results.Ok(conversions);
})
.WithName("ListReverseConversions")
.RequireAuthorization();

// --- Billing reconciliation: RMS-pushed signed adjustments (spec sections 7 & 8) applied to ---
// the consumer's wallet exactly like a recharge, plus the daily billing-data export AMISP needs
// to give RMS so it can compute its own shadow monthly bill and find any gap in the first place.
app.MapPost("/api/v1/consumers/{accountNumber}/reconciliation-adjustments", async (
    string accountNumber, ReconciliationAdjustmentRequest request, PrepaidEngineDbContext db,
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

    Audit(db, nameof(ReconciliationAdjustment), adjustment.Id.ToString(), "Applied", "system",
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
// Server-side searchable, keyset-paginated audit log. Read-only by design: there is no update or
// delete endpoint for audit entries anywhere in this API. Newest first; the cursor is
// "<OccurredAt ticks>_<Id>". q matches entity id / action / actor by prefix and details by substring.
app.MapGet("/api/v1/audit-entries/search", async (
    string? q, string? entityType, string? actor, DateTime? from, DateTime? to, string? after, int? pageSize,
    PrepaidEngineDbContext db) =>
{
    var size = Math.Clamp(pageSize ?? 25, 1, 100);
    var query = db.AuditEntries.AsNoTracking().AsQueryable();

    if (!string.IsNullOrWhiteSpace(entityType))
        query = query.Where(a => a.EntityType == entityType);
    if (!string.IsNullOrWhiteSpace(actor))
        query = query.Where(a => a.Actor == actor);
    if (from.HasValue)
        query = query.Where(a => a.OccurredAt >= DateTime.SpecifyKind(from.Value, DateTimeKind.Utc));
    if (to.HasValue)
        query = query.Where(a => a.OccurredAt < DateTime.SpecifyKind(to.Value.Date.AddDays(1), DateTimeKind.Utc));
    if (!string.IsNullOrWhiteSpace(q))
    {
        var term = q.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");
        var prefix = term + "%";
        var contains = "%" + term + "%";
        query = query.Where(a =>
            EF.Functions.ILike(a.EntityId, prefix) ||
            EF.Functions.ILike(a.Action, prefix) ||
            EF.Functions.ILike(a.Actor, prefix) ||
            (a.Details != null && EF.Functions.ILike(a.Details, contains)));
    }

    var totalCount = await query.CountAsync();

    if (!string.IsNullOrEmpty(after))
    {
        var parts = after.Split('_', 2);
        if (parts.Length != 2 || !long.TryParse(parts[0], out var ticks) || !Guid.TryParse(parts[1], out var afterId))
            return Results.BadRequest(new { error = "Invalid cursor." });
        var afterAt = new DateTime(ticks, DateTimeKind.Utc);
        query = query.Where(a => a.OccurredAt < afterAt || (a.OccurredAt == afterAt && a.Id.CompareTo(afterId) < 0));
    }

    var rows = await query
        .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
        .Take(size + 1)
        .Select(a => new { a.Id, a.EntityType, a.EntityId, a.Action, a.Actor, a.OldValue, a.NewValue, a.Details, a.OccurredAt })
        .ToListAsync();

    var hasMore = rows.Count > size;
    var items = hasMore ? rows.Take(size).ToList() : rows;
    var nextCursor = hasMore ? $"{items[^1].OccurredAt.Ticks}_{items[^1].Id}" : null;
    return Results.Ok(new { items, nextCursor, totalCount });
})
.WithName("SearchAuditEntries")
.RequireAuthorization();

// Totals plus the distinct entity types and actors that populate the filter dropdowns.
app.MapGet("/api/v1/audit-entries/summary", async (PrepaidEngineDbContext db) =>
{
    var since = DateTime.UtcNow.AddHours(-24);
    var total = await db.AuditEntries.CountAsync();
    var last24h = await db.AuditEntries.CountAsync(a => a.OccurredAt >= since);
    var entityTypes = await db.AuditEntries.Select(a => a.EntityType).Distinct().OrderBy(x => x).ToListAsync();
    var actors = await db.AuditEntries.Select(a => a.Actor).Distinct().OrderBy(x => x).Take(200).ToListAsync();
    return Results.Ok(new { Total = total, Last24Hours = last24h, EntityTypes = entityTypes, Actors = actors });
})
.WithName("GetAuditSummary")
.RequireAuthorization();

app.MapGet("/api/v1/audit-entries", async (PrepaidEngineDbContext db, string? entityType, string? entityId, DateTime? from, DateTime? to) =>
{
    var query = db.AuditEntries.AsQueryable();

    if (!string.IsNullOrWhiteSpace(entityId))
        query = query.Where(a => a.EntityId == entityId);
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
        .Take(string.IsNullOrWhiteSpace(entityId) ? int.MaxValue : 100)
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

// --- SLA monitoring (Phase 3): real performance against configurable targets, computed from ---
// timestamps this engine already records — see ISlaMonitoringService's doc comment.
app.MapGet("/api/v1/sla", async (ISlaMonitoringService slaMonitoring) =>
{
    var metrics = await slaMonitoring.GetSlaSummaryAsync();
    return Results.Ok(metrics);
})
.WithName("GetSlaSummary")
.RequireAuthorization();

// --- Revenue & Risk Indicators (Phase 3): real open-condition counts only — deliberately never
// a fabricated monetary "revenue protected" figure (see RiskIndicatorsSummary's doc comment).
app.MapGet("/api/v1/risk-indicators", async (PrepaidEngineDbContext db) =>
{
    var summary = new RiskIndicatorsSummary(
        OpenExceptions: await db.OperationalExceptions.CountAsync(e => e.Status == OperationalExceptionStatus.Open),
        ActiveBillingHolds: await db.MeterBillingControls.CountAsync(m => m.ActualBillingBlocked),
        UnresolvedMeterAlarms: await db.MeterAlarms.CountAsync(a => a.Status != MeterAlarmStatus.Resolved),
        DisconnectedConsumers: await db.Consumers.CountAsync(c => c.ConnectionStatus == ConnectionStatus.Disconnected),
        FailedEnergyValidations: await db.EnergyValidationResults.CountAsync(v => v.Status == EnergyValidationStatus.Fail));

    return Results.Ok(summary);
})
.WithName("GetRiskIndicators")
.RequireAuthorization();

// --- DLP billing pipeline: Daily Load Profile ingestion and the daily charge — see -------------
// IBillingEngineService's doc comment. The Load Survey (LS) hourly pipeline that used to also
// live in this section has been removed; DLP alone drives ongoing prepaid billing now.
app.MapPost("/api/v1/meter-data/dlp", async (DailyLoadProfileIngestRequest request, IBillingEngineService billingEngine) =>
{
    var result = await billingEngine.IngestDailyLoadProfileAsync(
        new DailyLoadProfileRequest(request.ConsumerId, request.MeterId, request.ProfileDate, request.GeneratedAt,
            request.StartCumulativeKwh, request.EndCumulativeKwh, request.SourceReference));

    return Results.Ok(result);
})
.WithName("IngestDailyLoadProfile")
.RequireAuthorization();

// Cross-consumer operator visibility into every Daily Load Profile — real, provisional, or
// billed — never hidden behind the daily settlement result alone.
app.MapGet("/api/v1/meter-data/dlp", async (Guid? consumerId, PrepaidEngineDbContext db) =>
{
    var query = db.DailyLoadProfiles.AsQueryable();
    if (consumerId.HasValue)
        query = query.Where(d => d.ConsumerId == consumerId.Value);

    var profiles = await (
        from d in query
        join consumer in db.Consumers on d.ConsumerId equals consumer.Id
        join meter in db.Meters on d.MeterId equals meter.Id
        orderby d.ProfileDate descending
        select new
        {
            d.Id,
            consumer.AccountNumber,
            consumer.Name,
            meter.MeterNumber,
            d.ProfileDate,
            d.GeneratedAt,
            d.ReceivedAt,
            d.StartCumulativeKwh,
            d.EndCumulativeKwh,
            d.TotalKwh,
            d.Status,
            d.IsProvisional,
            d.SourceReference,
        })
        .Take(500)
        .ToListAsync();

    return Results.Ok(profiles);
})
.WithName("ListDailyLoadProfiles")
.RequireAuthorization();

// --- MDMS data foundation: BP (register validation), LS (consumption intelligence), IP
// (instantaneous meter health), Events/Alarms, and cross-source energy validation. None of these
// bill anything — DLP above remains the sole daily billing driver. See
// IMeterDataIngestionService's doc comment for each source's role.

app.MapPost("/api/v1/meter-data/bp", async (RegisterReadingIngestRequest request, IMeterDataIngestionService meterData) =>
{
    var result = await meterData.IngestRegisterReadingAsync(
        new RegisterReadingRequest(request.ConsumerId, request.MeterId, request.ReadingTimestamp, request.CumulativeImportKwh, request.SourceReference));
    return Results.Ok(result);
})
.WithName("IngestRegisterReading")
.RequireAuthorization();

app.MapGet("/api/v1/meter-data/bp", async (Guid? consumerId, Guid? meterId, PrepaidEngineDbContext db) =>
{
    var query = db.RegisterReadings.AsQueryable();
    if (consumerId.HasValue) query = query.Where(r => r.ConsumerId == consumerId.Value);
    if (meterId.HasValue) query = query.Where(r => r.MeterId == meterId.Value);

    var readings = await (
        from r in query
        join consumer in db.Consumers on r.ConsumerId equals consumer.Id
        join meter in db.Meters on r.MeterId equals meter.Id
        orderby r.ReadingTimestamp descending
        select new { r.Id, consumer.AccountNumber, consumer.Name, meter.MeterNumber, r.ReadingTimestamp, r.CumulativeImportKwh, r.Status, r.ReceivedAt, r.SourceReference })
        .Take(500)
        .ToListAsync();

    return Results.Ok(readings);
})
.WithName("ListRegisterReadings")
.RequireAuthorization();

app.MapPost("/api/v1/meter-data/ls", async (LoadSurveyIntervalIngestRequest request, IMeterDataIngestionService meterData) =>
{
    var result = await meterData.IngestLoadSurveyIntervalAsync(
        new LoadSurveyIntervalRequest(request.ConsumerId, request.MeterId, request.IntervalStart, request.IntervalEnd, request.ImportKwh, request.SourceReference));
    return Results.Ok(result);
})
.WithName("IngestLoadSurveyInterval")
.RequireAuthorization();

app.MapGet("/api/v1/meter-data/ls", async (Guid? consumerId, Guid? meterId, DateTime? from, DateTime? to, PrepaidEngineDbContext db) =>
{
    var query = db.LoadSurveyIntervals.AsQueryable();
    if (consumerId.HasValue) query = query.Where(l => l.ConsumerId == consumerId.Value);
    if (meterId.HasValue) query = query.Where(l => l.MeterId == meterId.Value);
    if (from.HasValue) query = query.Where(l => l.IntervalStart >= from.Value);
    if (to.HasValue) query = query.Where(l => l.IntervalStart < to.Value);

    var intervals = await (
        from l in query
        join consumer in db.Consumers on l.ConsumerId equals consumer.Id
        join meter in db.Meters on l.MeterId equals meter.Id
        orderby l.IntervalStart descending
        select new { l.Id, consumer.AccountNumber, consumer.Name, meter.MeterNumber, l.IntervalStart, l.IntervalEnd, l.ImportKwh, l.SourceReference })
        .Take(1000)
        .ToListAsync();

    return Results.Ok(intervals);
})
.WithName("ListLoadSurveyIntervals")
.RequireAuthorization();

app.MapPost("/api/v1/meter-data/ip", async (InstantaneousReadingIngestRequest request, IMeterDataIngestionService meterData) =>
{
    var result = await meterData.IngestInstantaneousReadingAsync(
        new InstantaneousReadingRequest(request.ConsumerId, request.MeterId, request.Timestamp, request.VoltageVolts,
            request.CurrentAmps, request.PowerKw, request.PowerFactor, request.FrequencyHz, request.RelayStatus, request.SourceReference));
    return Results.Ok(result);
})
.WithName("IngestInstantaneousReading")
.RequireAuthorization();

// Latest IP reading per meter — meter-health snapshot, never a daily-billing input. The
// group-by-then-take-first step is done as its own query (translates cleanly against the base
// entity), then joined against Consumers/Meters in memory — EF Core's SQL translator cannot
// express a three-way join combined with "first row per group" in a single query.
app.MapGet("/api/v1/meter-data/ip/latest", async (Guid? consumerId, Guid? meterId, PrepaidEngineDbContext db) =>
{
    var query = db.InstantaneousReadings.AsQueryable();
    if (consumerId.HasValue) query = query.Where(i => i.ConsumerId == consumerId.Value);
    if (meterId.HasValue) query = query.Where(i => i.MeterId == meterId.Value);

    var latestReadings = await query
        .GroupBy(i => i.MeterId)
        .Select(g => g.OrderByDescending(i => i.Timestamp).First())
        .ToListAsync();

    var consumerIds = latestReadings.Select(r => r.ConsumerId).ToHashSet();
    var meterIds = latestReadings.Select(r => r.MeterId).ToHashSet();
    var consumers = await db.Consumers.Where(c => consumerIds.Contains(c.Id)).ToDictionaryAsync(c => c.Id);
    var meters = await db.Meters.Where(m => meterIds.Contains(m.Id)).ToDictionaryAsync(m => m.Id);

    var latestByMeter = latestReadings.Select(i => new
    {
        i.Id,
        AccountNumber = consumers[i.ConsumerId].AccountNumber,
        consumers[i.ConsumerId].Name,
        MeterNumber = meters[i.MeterId].MeterNumber,
        i.Timestamp,
        i.VoltageVolts, i.CurrentAmps, i.PowerKw, i.PowerFactor, i.FrequencyHz, i.RelayStatus,
    });

    return Results.Ok(latestByMeter);
})
.WithName("GetLatestInstantaneousReadings")
.RequireAuthorization();

app.MapPost("/api/v1/meter-data/events", async (MeterEventIngestRequest request, IMeterDataIngestionService meterData) =>
{
    var result = await meterData.IngestMeterEventAsync(
        new MeterEventRequest(request.ConsumerId, request.MeterId, request.EventCode, request.EventTimestamp, request.Description, request.SourceReference));
    return Results.Ok(result);
})
.WithName("IngestMeterEvent")
.RequireAuthorization();

app.MapGet("/api/v1/meter-data/events", async (Guid? consumerId, Guid? meterId, PrepaidEngineDbContext db) =>
{
    var query = db.MeterEvents.AsQueryable();
    if (consumerId.HasValue) query = query.Where(e => e.ConsumerId == consumerId.Value);
    if (meterId.HasValue) query = query.Where(e => e.MeterId == meterId.Value);

    var events = await (
        from e in query
        join consumer in db.Consumers on e.ConsumerId equals consumer.Id
        join meter in db.Meters on e.MeterId equals meter.Id
        orderby e.EventTimestamp descending
        select new { e.Id, consumer.AccountNumber, consumer.Name, meter.MeterNumber, e.EventCode, e.EventTimestamp, e.Description, e.Status })
        .Take(500)
        .ToListAsync();

    return Results.Ok(events);
})
.WithName("ListMeterEvents")
.RequireAuthorization();

app.MapPost("/api/v1/meter-data/alarms", async (MeterAlarmIngestRequest request, IMeterDataIngestionService meterData) =>
{
    var result = await meterData.IngestMeterAlarmAsync(
        new MeterAlarmRequest(request.ConsumerId, request.MeterId, request.AlarmCode, request.Severity, request.RaisedAt, request.SourceReference));
    return Results.Ok(result);
})
.WithName("IngestMeterAlarm")
.RequireAuthorization();

app.MapGet("/api/v1/meter-data/alarms", async (Guid? consumerId, Guid? meterId, MeterAlarmStatus? status, PrepaidEngineDbContext db) =>
{
    var query = db.MeterAlarms.AsQueryable();
    if (consumerId.HasValue) query = query.Where(a => a.ConsumerId == consumerId.Value);
    if (meterId.HasValue) query = query.Where(a => a.MeterId == meterId.Value);
    if (status.HasValue) query = query.Where(a => a.Status == status.Value);

    var alarms = await (
        from a in query
        join consumer in db.Consumers on a.ConsumerId equals consumer.Id
        join meter in db.Meters on a.MeterId equals meter.Id
        orderby a.RaisedAt descending
        select new
        {
            a.Id, consumer.AccountNumber, consumer.Name, meter.MeterNumber, a.AlarmCode, a.Severity, a.RaisedAt,
            a.Status, a.AcknowledgedAt, a.AcknowledgedBy, a.ResolvedAt, a.ResolutionNote,
        })
        .Take(500)
        .ToListAsync();

    return Results.Ok(alarms);
})
.WithName("ListMeterAlarms")
.RequireAuthorization();

app.MapPost("/api/v1/meter-data/alarms/{id:guid}/acknowledge", async (Guid id, AcknowledgeAlarmRequest request, PrepaidEngineDbContext db) =>
{
    var alarm = await db.MeterAlarms.FirstOrDefaultAsync(a => a.Id == id);
    if (alarm is null) return Results.NotFound();

    try
    {
        alarm.Acknowledge(request.AcknowledgedBy, DateTime.UtcNow);
    }
    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { alarm.Id, alarm.Status, alarm.AcknowledgedAt, alarm.AcknowledgedBy });
})
.WithName("AcknowledgeMeterAlarm")
.RequireAuthorization();

app.MapPost("/api/v1/meter-data/alarms/{id:guid}/resolve", async (Guid id, ResolveAlarmRequest request, PrepaidEngineDbContext db) =>
{
    var alarm = await db.MeterAlarms.FirstOrDefaultAsync(a => a.Id == id);
    if (alarm is null) return Results.NotFound();

    try
    {
        alarm.Resolve(request.ResolutionNote, DateTime.UtcNow);
    }
    catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
    {
        return Results.BadRequest(new { error = ex.Message });
    }

    await db.SaveChangesAsync();
    return Results.Ok(new { alarm.Id, alarm.Status, alarm.ResolvedAt, alarm.ResolutionNote });
})
.WithName("ResolveMeterAlarm")
.RequireAuthorization();

// DLP completeness for a given date across every active prepaid consumer — see
// DlpCompletenessStatus's doc comment for what each status means and how it should influence
// billing (surfaced operationally; this endpoint itself never blocks or triggers billing).
app.MapGet("/api/v1/meter-data/dlp-completeness", async (DateOnly date, IMeterDataIngestionService meterData) =>
{
    var rows = await meterData.GetDlpCompletenessAsync(date);
    return Results.Ok(rows);
})
.WithName("GetDlpCompleteness")
.RequireAuthorization();

// Cross-source energy validation (BP vs DLP, LS vs DLP, BP vs LS) for one consumer/meter/day —
// see IMeterDataIngestionService.EvaluateEnergyValidationAsync's doc comment. Evaluated on demand
// here rather than continuously, since it only makes sense once the day's DLP/BP/LS have arrived.
app.MapPost("/api/v1/meter-data/energy-validation", async (EvaluateEnergyValidationRequest request, IMeterDataIngestionService meterData) =>
{
    var outcomes = await meterData.EvaluateEnergyValidationAsync(request.ConsumerId, request.MeterId, request.ValidationDate);
    return Results.Ok(outcomes);
})
.WithName("EvaluateEnergyValidation")
.RequireAuthorization();

app.MapGet("/api/v1/meter-data/energy-validation", async (Guid? consumerId, Guid? meterId, EnergyValidationStatus? status, PrepaidEngineDbContext db) =>
{
    var query = db.EnergyValidationResults.AsQueryable();
    if (consumerId.HasValue) query = query.Where(v => v.ConsumerId == consumerId.Value);
    if (meterId.HasValue) query = query.Where(v => v.MeterId == meterId.Value);
    if (status.HasValue) query = query.Where(v => v.Status == status.Value);

    var results = await (
        from v in query
        join consumer in db.Consumers on v.ConsumerId equals consumer.Id
        join meter in db.Meters on v.MeterId equals meter.Id
        orderby v.ValidationDate descending
        select new
        {
            v.Id, consumer.AccountNumber, consumer.Name, meter.MeterNumber, v.ValidationDate, v.Rule,
            v.ExpectedValueKwh, v.ActualValueKwh, v.VarianceKwh, v.VariancePct, v.Status, v.Reason, v.EvaluatedAt,
        })
        .Take(500)
        .ToListAsync();

    return Results.Ok(results);
})
.WithName("ListEnergyValidationResults")
.RequireAuthorization();

// Two-stage daily DLP billing, dispatched automatically by BillingProcessingWorker within its
// two windows (8:30-9:30 AM / 12:30-1:30 PM); exposed here too so the demo can trigger either
// stage manually without waiting for the clock. `cutoff` lets a manual/demo call specify exactly
// which receipt-time boundary to bill against (defaults to 8:00 AM / 12:00 PM of `billingDate`'s
// following day, matching the worker's own real cutoffs) — see IBillingEngineService's doc
// comment for the full stage-1-vs-stage-2 rule.
app.MapPost("/api/v1/billing/daily/{billingDate}/stage1", async (DateOnly billingDate, DateTime? cutoff, IBillingEngineService billingEngine) =>
{
    var stage1Cutoff = cutoff ?? billingDate.AddDays(1).ToDateTime(new TimeOnly(8, 0), DateTimeKind.Utc);
    var results = await billingEngine.ProcessDailyStage1Async(billingDate, stage1Cutoff);
    return Results.Ok(results);
})
.WithName("ProcessDailyBillingStage1")
.RequireAuthorization();

app.MapPost("/api/v1/billing/daily/{billingDate}/stage2", async (DateOnly billingDate, DateTime? cutoff, IBillingEngineService billingEngine) =>
{
    var stage2Cutoff = cutoff ?? billingDate.AddDays(1).ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc);
    var results = await billingEngine.ProcessDailyStage2Async(billingDate, stage2Cutoff);
    return Results.Ok(results);
})
.WithName("ProcessDailyBillingStage2")
.RequireAuthorization();

app.MapPost("/api/v1/consumers/{consumerId:guid}/meter-replacement", async (
    Guid consumerId, MeterReplacementApiRequest request, IBillingEngineService billingEngine) =>
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
.RequireAuthorization();

// Cross-consumer operator view of every recorded meter replacement (spec §15-16) — the audit
// trail that exists specifically so an old meter's cumulative reading is never compared against
// a new meter's (they're different physical meters). Old/new meter numbers are resolved via a
// left join since OldMeterId is null for an initial Installed event (not currently produced by
// ReplaceMeterAsync, which only ever records Replaced, but the entity/join supports it).
app.MapGet("/api/v1/meter-replacements", async (PrepaidEngineDbContext db) =>
{
    var replacements = await (
        from a in db.MeterAssignments
        join consumer in db.Consumers on a.ConsumerId equals consumer.Id
        join newMeter in db.Meters on a.NewMeterId equals newMeter.Id
        join oldMeter in db.Meters on a.OldMeterId equals oldMeter.Id into oldMeterJoin
        from oldMeter in oldMeterJoin.DefaultIfEmpty()
        orderby a.RecordedAt descending
        select new
        {
            a.Id,
            consumer.AccountNumber,
            consumer.Name,
            a.EventType,
            OldMeterNumber = oldMeter != null ? oldMeter.MeterNumber : null,
            NewMeterNumber = newMeter.MeterNumber,
            a.EffectiveFrom,
            a.OldMeterClosingReadingKwh,
            a.NewMeterOpeningReadingKwh,
            a.Reason,
            a.RecordedAt,
        })
        .ToListAsync();

    return Results.Ok(replacements);
})
.WithName("ListMeterReplacements")
.RequireAuthorization();

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

// Cross-consumer operator view — GetConsumerNotifications above is scoped to one consumer (e.g.
// for a future Consumer 360 section); this backs a standalone Notification History page.
app.MapGet("/api/v1/notifications", async (PrepaidEngineDbContext db) =>
{
    var notifications = await (
        from n in db.NotificationEvents
        join consumer in db.Consumers on n.ConsumerId equals consumer.Id
        orderby n.CreatedAt descending
        select new
        {
            n.Id,
            consumer.AccountNumber,
            consumer.Name,
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
.WithName("ListNotifications")
.RequireAuthorization();

// Operator visibility into MeterBillingControl holds (spec §8) — without this, the clear
// endpoint below has nothing for an operator to act against.
app.MapGet("/api/v1/meter-data/billing-holds", async (bool? activeOnly, PrepaidEngineDbContext db) =>
{
    var query = db.MeterBillingControls.AsQueryable();
    if (activeOnly ?? true)
        query = query.Where(c => c.ActualBillingBlocked);

    var holds = await (
        from c in query
        join consumer in db.Consumers on c.ConsumerId equals consumer.Id
        join meter in db.Meters on c.MeterId equals meter.Id
        orderby c.BlockedAt descending
        select new
        {
            c.Id,
            c.MeterId,
            consumer.AccountNumber,
            consumer.Name,
            meter.MeterNumber,
            c.ActualBillingBlocked,
            c.BlockReason,
            c.BlockedAt,
            c.ClearedAt,
        })
        .ToListAsync();

    return Results.Ok(holds);
})
.WithName("ListMeterBillingHolds")
.RequireAuthorization();

// The operator-facing clear API the LS/DLP spec §8 calls for — a documented follow-up when that
// spec landed, built now. Requires a mandatory resolution note (this project's established
// mandatory-reason discipline — see ConnectivityCommand.Reason) and leaves an audit trail; actual
// billing for the meter resumes (both hourly LS and daily DLP) the moment the hold clears.
app.MapPost("/api/v1/meter-data/{meterId:guid}/billing-hold/clear", async (
    Guid meterId, ResolutionRequest request, PrepaidEngineDbContext db) =>
{
    if (string.IsNullOrWhiteSpace(request.Note))
        return Results.BadRequest(new { error = "A resolution note is required to clear a billing hold." });

    var control = await db.MeterBillingControls.FirstOrDefaultAsync(c => c.MeterId == meterId && c.ActualBillingBlocked);
    if (control is null)
        return Results.NotFound(new { error = "No active billing hold exists for this meter." });

    control.Clear(DateTime.UtcNow);

    Audit(db, nameof(MeterBillingControl), control.Id.ToString(), "BillingHoldCleared", "system", details: request.Note);
    await db.SaveChangesAsync();

    return Results.Ok(new { control.Id, control.MeterId, control.ActualBillingBlocked, control.ClearedAt });
})
.WithName("ClearMeterBillingHold")
.RequireAuthorization();

// Bulk variant of the endpoint above — the same mandatory-reason discipline, one shared
// resolution note applied to every meter in the batch (an operator clearing several holds at
// once is asserting one common finding, e.g. "confirmed with field crew: all listed meters were
// reset during today's maintenance window" — if the reasons genuinely differ per meter, that's a
// signal to clear them individually with the single-hold endpoint instead, not something this
// endpoint should paper over with per-item notes). One bad meter ID in the batch does not fail
// the rest — each is independently validated and reported, matching the batch-processing pattern
// already used by POST /api/v1/conversions.
app.MapPost("/api/v1/meter-data/billing-holds/clear-bulk", async (
    BulkClearBillingHoldsRequest request, PrepaidEngineDbContext db) =>
{
    if (request.MeterIds is null || request.MeterIds.Count == 0)
        return Results.BadRequest(new { error = "At least one meter ID is required." });
    if (string.IsNullOrWhiteSpace(request.Note))
        return Results.BadRequest(new { error = "A resolution note is required to clear a billing hold." });

    var results = new List<object>();

    foreach (var meterId in request.MeterIds.Distinct())
    {
        var control = await db.MeterBillingControls.FirstOrDefaultAsync(c => c.MeterId == meterId && c.ActualBillingBlocked);
        if (control is null)
        {
            results.Add(new { MeterId = meterId, Cleared = false, Error = "No active billing hold exists for this meter." });
            continue;
        }

        control.Clear(DateTime.UtcNow);
        Audit(db, nameof(MeterBillingControl), control.Id.ToString(), "BillingHoldCleared", "system", details: request.Note);
        results.Add(new { MeterId = meterId, Cleared = true, Error = (string?)null, control.Id, control.ClearedAt });
    }

    await db.SaveChangesAsync();

    return Results.Ok(results);
})
.WithName("ClearMeterBillingHoldsBulk")
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
/// <summary>RMS's finalized postpaid->prepaid conversion request parameter set, plus the
/// pre-existing InitialReading/InitialReadingDateTime (the 1st-of-conversion-month opening
/// reading this project's conversion opening-bill calculation requires).</summary>
public record ConversionRequestItem(
    string TransactionId,
    string MeterSerialNumber,
    string ConsumerNumber,
    ConversionConsumerType ConsumerType,
    decimal InitialReading,
    DateTime InitialReadingDateTime,
    DateTime ConversionDate,
    DateTime LastReadingDate,
    DateTime LastBillingDate,
    decimal LastBillFrKwh,
    decimal LastBillFrKvah,
    decimal LastBillMaxDemandKw,
    decimal OutstandingAmount,
    ConversionMeterStatus MeterStatus,
    bool IsPermanentConsumer,
    decimal FoaAmount,
    decimal DiaAmount,
    DateTime? TemporaryDisconnectionDate = null,
    DateTime? ReconnectionDate = null,
    string? RequestType = "PRE");

/// <summary>MDMS's synchronous per-request acknowledgement back to RMS.</summary>
/// <param name="PaymentModeChangeStatus">The MDMS -> HES -> Meter command's final status, when a
/// command was actually dispatched (null for requests rejected before dispatch, e.g. a NET meter).</param>
public record ConversionResponseItem(string TransactionId, string ConsumerNumber, string ResponseCode, string? ResponseMessage, string? PaymentModeChangeStatus);

/// <param name="Note">Required resolution note, mirroring ConnectivityCommand's mandatory Reason pattern.</param>
public record ResolutionRequest(string Note);

/// <param name="MeterIds">The meters whose active billing hold should be cleared.</param>
/// <param name="Note">Required — one shared resolution note applied to every meter in the batch.</param>
public record BulkClearBillingHoldsRequest(List<Guid> MeterIds, string Note);

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

// --- Tariff governance request DTOs -------------------------------------------------------------
/// <summary>One proposed energy slab. <see cref="StartTime"/>/<see cref="EndTime"/> equivalents
/// for ToD periods are plain "HH:mm:ss"-parseable strings (see <see cref="TouPeriodInput"/>) since
/// minimal APIs don't model-bind <see cref="TimeSpan"/> from JSON as cleanly as ISO-8601 duration.</summary>
public record TariffSlabInput(decimal FromKwh, decimal? UpToKwh, decimal RatePerKwh);

public record TouPeriodInput(string Label, string StartTime, string EndTime, decimal RatePerKvah);

public record CreateTariffChangeRequestBody(
    Guid? SupersedesTariffId,
    string ProposedName,
    ConsumerCategory ProposedCategory,
    IReadOnlyList<TariffSlabInput> ProposedSlabs,
    decimal ProposedFixedChargePerUnitPerMonth,
    decimal ProposedPrepaidEnergyRebatePercent,
    decimal ProposedEmergencyCreditLimit,
    decimal? ProposedMinVendAmountSinglePhase = null,
    decimal? ProposedMaxVendAmountSinglePhase = null,
    decimal? ProposedMinVendAmountThreePhase = null,
    decimal? ProposedMaxVendAmountThreePhase = null,
    IReadOnlyList<TouPeriodInput>? ProposedTouPeriods = null);

public record UpdateTariffChangeRequestBody(
    string ProposedName,
    ConsumerCategory ProposedCategory,
    IReadOnlyList<TariffSlabInput> ProposedSlabs,
    decimal ProposedFixedChargePerUnitPerMonth,
    decimal ProposedPrepaidEnergyRebatePercent,
    decimal ProposedEmergencyCreditLimit,
    decimal? ProposedMinVendAmountSinglePhase = null,
    decimal? ProposedMaxVendAmountSinglePhase = null,
    decimal? ProposedMinVendAmountThreePhase = null,
    decimal? ProposedMaxVendAmountThreePhase = null,
    IReadOnlyList<TouPeriodInput>? ProposedTouPeriods = null);

public record SubmitTariffChangeRequestBody(string ChangeReason);

public record ApproveTariffChangeRequestBody(DateTime CommencementDate);

public record RejectTariffChangeRequestBody(string RejectionReason);

/// <summary>POST /api/v1/meter-data/dlp request body.</summary>
public record DailyLoadProfileIngestRequest(
    Guid ConsumerId, Guid MeterId, DateOnly ProfileDate, DateTime GeneratedAt,
    decimal StartCumulativeKwh, decimal EndCumulativeKwh, string? SourceReference = null);

/// <summary>POST /api/v1/consumers/{consumerId}/meter-replacement request body.</summary>
public record MeterReplacementApiRequest(
    string NewMeterNumber, MeterPhase Phase, DateTime EffectiveFrom,
    decimal OldMeterClosingReadingKwh, decimal NewMeterOpeningReadingKwh, string Reason);

/// <summary>POST /api/v1/conversions/reverse request body.</summary>
public record ReverseConversionApiRequest(string ConsumerNumber, string Reason, string RequestedBy);

// --- MDMS data foundation request DTOs (BP/LS/IP/Events/Alarms/energy-validation) -------------
public record RegisterReadingIngestRequest(Guid ConsumerId, Guid MeterId, DateTime ReadingTimestamp, decimal CumulativeImportKwh, string? SourceReference = null);

public record LoadSurveyIntervalIngestRequest(Guid ConsumerId, Guid MeterId, DateTime IntervalStart, DateTime IntervalEnd, decimal ImportKwh, string? SourceReference = null);

public record InstantaneousReadingIngestRequest(
    Guid ConsumerId, Guid MeterId, DateTime Timestamp, decimal VoltageVolts, decimal CurrentAmps,
    decimal PowerKw, decimal PowerFactor, decimal FrequencyHz, MeterRelayStatus RelayStatus, string? SourceReference = null);

public record MeterEventIngestRequest(Guid ConsumerId, Guid MeterId, MeterEventCode EventCode, DateTime EventTimestamp, string? Description = null, string? SourceReference = null);

public record MeterAlarmIngestRequest(Guid ConsumerId, Guid MeterId, MeterAlarmCode AlarmCode, MeterAlarmSeverity Severity, DateTime RaisedAt, string? SourceReference = null);

public record AcknowledgeAlarmRequest(string AcknowledgedBy);

public record ResolveAlarmRequest(string ResolutionNote);

public record EvaluateEnergyValidationRequest(Guid ConsumerId, Guid MeterId, DateOnly ValidationDate);

// Exposed so WebApplicationFactory-based integration tests can bootstrap this Api project.
public partial class Program { }
