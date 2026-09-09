using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Api.Auth;
using PrepaidEngine.Application.Rms;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
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

// Basic auth for the demo endpoints only — a stop-gap, not a substitute for real
// authentication before any shared/production exposure (see docs/assumptions-and-security.md).
builder.Services.AddAuthentication("Basic")
    .AddScheme<BasicAuthenticationSchemeOptions, BasicAuthenticationHandler>("Basic", null);
builder.Services.AddAuthorization();

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
}

app.UseHttpsRedirection();

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
        .Select(b => new { b.Id, b.Amount, b.AmountPaid, b.Status, b.GeneratedAt })
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

// Recharge flow, orchestrated through IRmsClient (MockRmsClient for now — see the TODO
// above). RMS remains authoritative: this endpoint only credits the wallet once RMS reports
// Success, and never re-credits for a repeated idempotency key or a duplicated RMS reference.
app.MapPost("/api/v1/consumers/{accountNumber}/recharge", async (
    string accountNumber,
    RechargeRequest request,
    PrepaidEngineDbContext db,
    IRmsClient rmsClient) =>
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
        return Results.Ok(new
        {
            existing.RmsReferenceId,
            Status = existing.Status.ToString(),
            WalletBalance = consumer.Wallet.Balance,
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
            await db.SaveChangesAsync();
            return Results.Ok(new { recharge.RmsReferenceId, Status = recharge.Status.ToString(), WalletBalance = consumer.Wallet.Balance });

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

// Exposed so WebApplicationFactory-based integration tests can bootstrap this Api project.
public partial class Program { }
