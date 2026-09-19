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

/// <summary>Connectivity endpoints, moved out of Program.cs unchanged.</summary>
public static class ConnectivityEndpoints
{
    public static void MapConnectivityEndpoints(this WebApplication app)
    {
        // RC/DC read endpoints — real ConnectivityCommand records across all consumers.
        app.MapGet("/api/v1/connectivity-commands", async (HttpContext http, string? accountNumber, PrepaidEngineDbContext db) =>
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
                .ToCappedListAsync(http);

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
            ClaimsPrincipal user,
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

            Audit(db, nameof(ConnectivityCommand), command.Id.ToString(), "Retried", user.Identity?.Name ?? "unknown",
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
        .RequireAuthorization("Operations");
    }
}
