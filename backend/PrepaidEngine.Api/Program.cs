using Microsoft.EntityFrameworkCore;
using PrepaidEngine.Application.Rms;
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

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
    .WithName("Health");

// Demo/local-only read endpoints — no authentication yet (see docs/assumptions-and-security.md).
// Do not expose these unauthenticated outside local development.
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
.WithName("ListConsumers");

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
.WithName("GetConsumerByAccountNumber");

app.Run();

// Exposed so WebApplicationFactory-based integration tests can bootstrap this Api project.
public partial class Program { }
