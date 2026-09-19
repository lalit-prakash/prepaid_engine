namespace PrepaidEngine.Application.Wallets;

/// <summary>
/// What counts as a "low balance", bound from the "LowBalance" configuration section.
///
/// Unset (the default) keeps the long-standing behaviour: screens and counts treat a wallet as low when its balance is
/// below that wallet's own emergency credit limit, and the daily billing run warns a consumer when the balance falls
/// below Rs.100. Setting <see cref="ThresholdRs"/> replaces both with one figure, so the dashboard, the consumer list,
/// the consumer page and the low-balance SMS all agree.
/// </summary>
public sealed class LowBalanceOptions
{
    public const string SectionName = "LowBalance";

    /// <summary>The balance, in rupees, below which a wallet is low. Null means use the defaults described above.</summary>
    public decimal? ThresholdRs { get; set; }

    /// <summary>Balance below which the billing run queues a low-balance notification.</summary>
    public const decimal DefaultNotificationThreshold = 100m;

    public decimal NotificationThreshold => ThresholdRs ?? DefaultNotificationThreshold;

    /// <summary>True when the configured figure is usable (absent, or zero or more).</summary>
    public bool IsValid => ThresholdRs is null or >= 0;
}
