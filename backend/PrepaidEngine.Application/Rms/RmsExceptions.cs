namespace PrepaidEngine.Application.Rms;

/// <summary>
/// Thrown by an <see cref="IRmsClient"/> implementation when RMS could not be reached at all
/// (timeout, connection failure, 5xx, etc.) — as opposed to <see cref="RmsRechargeStatus.Failed"/>,
/// which is a definite outcome RMS itself reported.
/// </summary>
public class RmsUnavailableException : Exception
{
    public RmsUnavailableException(string message) : base(message)
    {
    }

    public RmsUnavailableException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

/// <summary>
/// Thrown when querying RMS for a transaction reference it has no record of.
/// </summary>
public class RmsTransactionNotFoundException : Exception
{
    public RmsTransactionNotFoundException(string rmsReferenceId)
        : base($"RMS has no record of transaction '{rmsReferenceId}'.")
    {
    }
}
