namespace PrepaidEngine.Application.Conversion;

/// <summary>
/// Port to whatever actually carries a payment-mode-change command down the MDMS → HES → Meter
/// chain and relays the meter's acknowledgement back. No such integration exists in this project
/// yet; only a mock/simulator implementation is registered (see
/// <c>MockPaymentModeChangeClient</c> in the Infrastructure layer), mirroring how
/// <c>PrepaidEngine.Application.Connectivity.IConnectivityCommandClient</c> relates to
/// <c>MockConnectivityCommandClient</c>.
/// </summary>
public interface IPaymentModeChangeClient
{
    /// <summary>
    /// Dispatches a payment-mode-change command for the given consumer/meter. Implementations
    /// should treat this as fire-and-await-acknowledgement for a single attempt.
    /// </summary>
    Task<PaymentModeChangeResult> ChangePaymentModeAsync(
        PaymentModeChangeRequest request, CancellationToken cancellationToken = default);
}

public enum PaymentModeChangeOutcome
{
    Acknowledged,
    Failed,
    TimedOut,
}

/// <summary>
/// <paramref name="InitialReading"/>/<paramref name="InitialReadingDateTime"/>/
/// <paramref name="ConversionDate"/> exist purely so a mock implementation without a real meter
/// to ask can fabricate a plausible current reading on acknowledgement — a real implementation
/// would ignore them and simply relay whatever the meter reports.
/// </summary>
public record PaymentModeChangeRequest(
    Guid ConsumerId,
    string MeterSerialNumber,
    string CorrelationId,
    decimal InitialReading,
    DateTime InitialReadingDateTime,
    DateTime ConversionDate);

/// <summary>
/// <paramref name="MeterReadingAtConversion"/> is populated only when
/// <paramref name="Outcome"/> is <see cref="PaymentModeChangeOutcome.Acknowledged"/> — the
/// meter's cumulative reading (kWh) at the moment it switched to prepaid mode.
/// </summary>
public record PaymentModeChangeResult(PaymentModeChangeOutcome Outcome, decimal? MeterReadingAtConversion, string? Message);
