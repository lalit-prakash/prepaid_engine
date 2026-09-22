using PrepaidEngine.Domain.Entities;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

public class FppasRateNotificationTests
{
    [Fact]
    public void The_applicable_billing_month_is_one_month_after_the_notification_month()
    {
        var n = new FppasRateNotification(Guid.NewGuid(), -0.14m, new DateTime(2026, 6, 15));
        Assert.Equal(new DateOnly(2026, 7, 1), n.ApplicableBillingMonth);
    }

    [Fact]
    public void A_notification_at_the_start_of_a_month_still_applies_to_the_next_month()
    {
        var n = new FppasRateNotification(Guid.NewGuid(), 0.0665m, new DateTime(2026, 10, 1));
        Assert.Equal(new DateOnly(2026, 11, 1), n.ApplicableBillingMonth);
    }

    [Fact]
    public void Renotifying_within_the_same_billing_month_replaces_the_rate()
    {
        var n = new FppasRateNotification(Guid.NewGuid(), -0.14m, new DateTime(2026, 6, 5));
        n.Renotify(-0.10m, new DateTime(2026, 6, 20));

        Assert.Equal(-0.10m, n.RateFraction);
        Assert.Equal(new DateTime(2026, 6, 20), n.NotifiedAt);
        Assert.Equal(new DateOnly(2026, 7, 1), n.ApplicableBillingMonth);
    }

    [Fact]
    public void Renotifying_into_a_different_billing_month_is_rejected()
    {
        var n = new FppasRateNotification(Guid.NewGuid(), -0.14m, new DateTime(2026, 6, 5));
        Assert.Throws<ArgumentException>(() => n.Renotify(-0.10m, new DateTime(2026, 7, 5)));
    }
}
