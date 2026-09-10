namespace PrepaidEngine.Application.MeterCommands;

/// <summary>
/// Outcome of dispatching a credit command to the meter-command layer. Deliberately has no
/// "Success" value that skips a real acknowledgement — see <see cref="IMeterCommandClient"/>.
/// </summary>
public enum MeterCommandOutcome
{
    Acknowledged,
    Failed,
    TimedOut,
}

/// <summary>
/// A request to credit a consumer's meter, dispatched only after RMS has confirmed the
/// underlying recharge payment.
/// </summary>
/// <param name="ConsumerId">The Prepaid Engine's identifier for the consumer.</param>
/// <param name="CreditAmount">Amount to credit onto the meter.</param>
/// <param name="CorrelationId">Correlation id propagated through logs/telemetry for this dispatch.</param>
public record SendCreditCommandRequest(
    Guid ConsumerId,
    decimal CreditAmount,
    string CorrelationId);

/// <summary>
/// The meter-command layer's response to a dispatched credit command.
/// </summary>
/// <param name="Outcome">What actually happened, per <see cref="MeterCommandOutcome"/>.</param>
/// <param name="Message">Human-readable detail, e.g. a failure or timeout reason.</param>
public record SendCreditCommandResult(
    MeterCommandOutcome Outcome,
    string? Message);
