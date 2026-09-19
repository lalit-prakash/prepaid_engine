using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using PrepaidEngine.Application.MeterCommands;
using PrepaidEngine.Domain.Entities;
using PrepaidEngine.Domain.Enums;
using PrepaidEngine.Infrastructure.MeterCommands;
using PrepaidEngine.Infrastructure.Persistence;

namespace PrepaidEngine.Tests.MeterCommands;

/// <summary>
/// A recharge only queues its meter credit command; the dispatcher sends it, claims each command so it is sent
/// once, records the outcome, raises operator exceptions for failures, and times out commands whose sender died.
/// </summary>
public class MeterCommandDispatcherTests : IDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private readonly PrepaidEngineDbContext _db;
    private readonly ScriptedClient _client = new();
    private readonly MeterCommandDispatcher _dispatcher;

    public MeterCommandDispatcherTests()
    {
        _connection.Open();
        _db = new PrepaidEngineDbContext(new DbContextOptionsBuilder<PrepaidEngineDbContext>().UseSqlite(_connection).Options);
        _db.Database.EnsureCreated();
        _dispatcher = new MeterCommandDispatcher(_db, _client, NullLogger<MeterCommandDispatcher>.Instance);
    }

    public void Dispose()
    {
        _db.Dispose();
        _connection.Dispose();
    }

    private sealed class ScriptedClient : IMeterCommandClient
    {
        public Func<SendCreditCommandRequest, SendCreditCommandResult> Respond { get; set; }
            = _ => new SendCreditCommandResult(MeterCommandOutcome.Acknowledged, "ok", "EXT-1", "00");
        public List<SendCreditCommandRequest> Sent { get; } = new();

        public Task<SendCreditCommandResult> SendCreditCommandAsync(SendCreditCommandRequest request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult(Respond(request));
        }
    }

    private async Task<MeterCommand> QueueAsync(string account, DateTime? createdAt = null)
    {
        var meter = new SmartMeter(Guid.NewGuid(), "M-" + account, MeterPhase.SinglePhase);
        var consumer = new Consumer(Guid.NewGuid(), account, account, "Street", meter, 1m);
        var recharge = new RechargeTransaction(Guid.NewGuid(), consumer.Id, 500m, "RMS-" + account, DateTime.UtcNow);
        var command = new MeterCommand(Guid.NewGuid(), consumer.Id, recharge.Id, 500m, createdAt ?? DateTime.UtcNow);
        _db.Meters.Add(meter);
        _db.Consumers.Add(consumer);
        _db.RechargeTransactions.Add(recharge);
        _db.MeterCommands.Add(command);
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        return command;
    }

    private Task<MeterCommand> ReloadAsync(Guid id) => _db.MeterCommands.AsNoTracking().SingleAsync(m => m.Id == id);

    [Fact]
    public async Task Sends_a_queued_command_and_records_the_acknowledgement()
    {
        var queued = await QueueAsync("A1");

        var sent = await _dispatcher.DispatchPendingAsync(10);

        Assert.Equal(1, sent);
        var saved = await ReloadAsync(queued.Id);
        Assert.Equal(MeterCommandStatus.Acknowledged, saved.Status);
        Assert.Equal("EXT-1", saved.ExternalCommandId);
        Assert.NotNull(saved.SentAt);
        Assert.NotNull(saved.AcknowledgedAt);
        Assert.Equal(500m, Assert.Single(_client.Sent).CreditAmount);
    }

    [Fact]
    public async Task A_failed_command_is_recorded_and_raises_an_operator_exception()
    {
        var queued = await QueueAsync("A1");
        _client.Respond = _ => new SendCreditCommandResult(MeterCommandOutcome.Failed, "Meter offline", null, "E07");

        await _dispatcher.DispatchPendingAsync(10);

        var saved = await ReloadAsync(queued.Id);
        Assert.Equal(MeterCommandStatus.Failed, saved.Status);
        Assert.Equal("Meter offline", saved.ErrorMessage);
        var exception = await _db.OperationalExceptions.SingleAsync();
        Assert.Equal(queued.Id, exception.SourceId);
        Assert.Contains("failed", exception.Description);
    }

    [Fact]
    public async Task A_timed_out_command_is_recorded_and_raises_an_operator_exception()
    {
        var queued = await QueueAsync("A1");
        _client.Respond = _ => new SendCreditCommandResult(MeterCommandOutcome.TimedOut, null);

        await _dispatcher.DispatchPendingAsync(10);

        Assert.Equal(MeterCommandStatus.TimedOut, (await ReloadAsync(queued.Id)).Status);
        Assert.Single(await _db.OperationalExceptions.ToListAsync());
    }

    [Fact]
    public async Task A_command_is_never_sent_twice()
    {
        await QueueAsync("A1");

        await _dispatcher.DispatchPendingAsync(10);
        var second = await _dispatcher.DispatchPendingAsync(10);

        Assert.Equal(0, second);
        Assert.Single(_client.Sent);
    }

    [Fact]
    public async Task Only_the_first_of_two_racing_claims_sends()
    {
        var queued = await QueueAsync("A1");
        // Another instance claims the command between our listing and our claim.
        await _db.MeterCommands.Where(m => m.Id == queued.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MeterCommandStatus.Sent).SetProperty(m => m.SentAt, DateTime.UtcNow));

        var sent = await _dispatcher.DispatchPendingAsync(10);

        Assert.Equal(0, sent);
        Assert.Empty(_client.Sent);
    }

    [Fact]
    public async Task Sends_oldest_first_and_no_more_than_the_batch_size()
    {
        var oldest = await QueueAsync("OLD", DateTime.UtcNow.AddMinutes(-30));
        await QueueAsync("MID", DateTime.UtcNow.AddMinutes(-20));
        var newest = await QueueAsync("NEW", DateTime.UtcNow.AddMinutes(-10));

        var sent = await _dispatcher.DispatchPendingAsync(2);

        Assert.Equal(2, sent);
        Assert.Equal(MeterCommandStatus.Acknowledged, (await ReloadAsync(oldest.Id)).Status);
        Assert.Equal(MeterCommandStatus.Queued, (await ReloadAsync(newest.Id)).Status);
    }

    [Fact]
    public async Task A_retried_command_is_sent_again_with_a_key_for_that_attempt()
    {
        var queued = await QueueAsync("A1");
        _client.Respond = _ => new SendCreditCommandResult(MeterCommandOutcome.Failed, "x");
        await _dispatcher.DispatchPendingAsync(10);

        var failed = await _db.MeterCommands.SingleAsync();
        failed.Retry();
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
        _client.Respond = _ => new SendCreditCommandResult(MeterCommandOutcome.Acknowledged, "ok");
        await _dispatcher.DispatchPendingAsync(10);

        Assert.Equal(MeterCommandStatus.Acknowledged, (await ReloadAsync(queued.Id)).Status);
        Assert.Equal(2, _client.Sent.Count);
        Assert.NotEqual(_client.Sent[0].CorrelationId, _client.Sent[1].CorrelationId);
    }

    [Fact]
    public async Task A_command_stuck_in_sent_is_timed_out_with_an_exception()
    {
        var queued = await QueueAsync("A1");
        await _db.MeterCommands.Where(m => m.Id == queued.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MeterCommandStatus.Sent).SetProperty(m => m.SentAt, DateTime.UtcNow.AddHours(-1)));

        var recovered = await _dispatcher.RecoverStuckAsync(TimeSpan.FromMinutes(10));

        Assert.Equal(1, recovered);
        Assert.Equal(MeterCommandStatus.TimedOut, (await ReloadAsync(queued.Id)).Status);
        Assert.Contains("check the meter", (await _db.OperationalExceptions.SingleAsync()).Description);
    }

    [Fact]
    public async Task A_recently_sent_command_is_left_alone()
    {
        var queued = await QueueAsync("A1");
        await _db.MeterCommands.Where(m => m.Id == queued.Id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.Status, MeterCommandStatus.Sent).SetProperty(m => m.SentAt, DateTime.UtcNow.AddMinutes(-1)));

        Assert.Equal(0, await _dispatcher.RecoverStuckAsync(TimeSpan.FromMinutes(10)));
        Assert.Equal(MeterCommandStatus.Sent, (await ReloadAsync(queued.Id)).Status);
    }
}
