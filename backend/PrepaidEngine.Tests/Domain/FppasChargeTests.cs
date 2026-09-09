using System.Linq;
using PrepaidEngine.Domain.Entities;
using Xunit;

namespace PrepaidEngine.Tests.Domain;

/// <summary>
/// Regression tests reproducing MePDCL's own two worked FPPAS examples verbatim
/// ("Prepaid bill calculation.xlsx", sheet "FPPAS Calculation") — a negative-FAC case and a
/// positive-FAC case.
/// </summary>
public class FppasChargeTests
{
    [Fact]
    public void TotalAmount_NegativeFacCase_MatchesReferenceWorkbook()
    {
        // "-14% FAC rate came from utility on 28th May, imposed on the EC of the 1st May bill,
        // which is 6000. The FAC to be billed on 1st June, 2026 is -840 rupees."
        var charge = new FppasCharge(Guid.NewGuid(), sourceEnergyCharge: 6000m, rateFraction: -0.14m, notifiedAt: new DateTime(2026, 5, 28));

        Assert.Equal(-840.00m, charge.TotalAmount);
    }

    [Fact]
    public void AllocateAcrossDays_NegativeFacCase_MatchesReferenceWorkbook()
    {
        // "But we cannot give the amount at one time, so it will be pro-rated in the whole
        // June month" — June has 30 days; the workbook shows -28 on every single day.
        var charge = new FppasCharge(Guid.NewGuid(), sourceEnergyCharge: 6000m, rateFraction: -0.14m, notifiedAt: new DateTime(2026, 5, 28));

        var allocations = charge.AllocateAcrossDays(daysInBillingMonth: 30);

        Assert.Equal(30, allocations.Count);
        Assert.All(allocations, a => Assert.Equal(-28.00m, a));
        Assert.Equal(charge.TotalAmount, allocations.Sum());
    }

    [Fact]
    public void TotalAmount_PositiveFacCase_MatchesReferenceWorkbook()
    {
        // "6.65% FAC rate came from utility on 29th Sept, imposed on the EC of the 1st Sept
        // bill, which is 3250. The FAC to be billed on 1st Oct, 2026 is 216.125 rupees."
        var charge = new FppasCharge(Guid.NewGuid(), sourceEnergyCharge: 3250m, rateFraction: 0.0665m, notifiedAt: new DateTime(2026, 9, 29));

        Assert.Equal(216.125m, charge.TotalAmount);
    }

    [Fact]
    public void AllocateAcrossDays_PositiveFacCase_MatchesReferenceWorkbook()
    {
        // October has 31 days; the workbook shows 6.971774193548387... on every day (full
        // precision, not rounded).
        var charge = new FppasCharge(Guid.NewGuid(), sourceEnergyCharge: 3250m, rateFraction: 0.0665m, notifiedAt: new DateTime(2026, 9, 29));

        var allocations = charge.AllocateAcrossDays(daysInBillingMonth: 31);

        Assert.Equal(31, allocations.Count);
        Assert.All(allocations, a => Assert.Equal(6.971774193548387m, a, 12));
        Assert.Equal(charge.TotalAmount, allocations.Sum(), 10);
    }

    [Theory]
    [InlineData(2026, 5, 2026, 6, 1)]  // notified against the May-1 bill -> applied to the June-1 bill
    [InlineData(2026, 9, 2026, 10, 1)] // notified against the Sept-1 bill -> applied to the Oct-1 bill
    public void DetermineApplicableBillingMonth_IsOneMonthAfterTheSourceBillDate(
        int sourceYear, int sourceMonth, int expectedYear, int expectedMonth, int expectedDay)
    {
        var sourceBillDate = new DateTime(sourceYear, sourceMonth, 1);

        var applicable = FppasCharge.DetermineApplicableBillingMonth(sourceBillDate);

        Assert.Equal(new DateTime(expectedYear, expectedMonth, expectedDay), applicable);
    }

    [Fact]
    public void AllocateAcrossDaysRoundedToCents_SumsExactlyToTotalAmount_EvenWhenNotEvenlyDivisible()
    {
        // 216.125 / 31 does not divide into a finite number of paise; the rounded allocation
        // must still reconcile to the cent by construction (last day absorbs the residual).
        var charge = new FppasCharge(Guid.NewGuid(), sourceEnergyCharge: 3250m, rateFraction: 0.0665m, notifiedAt: new DateTime(2026, 9, 29));

        var allocations = charge.AllocateAcrossDaysRoundedToCents(daysInBillingMonth: 31);

        Assert.Equal(31, allocations.Count);
        Assert.Equal(Math.Round(charge.TotalAmount, 2, MidpointRounding.AwayFromZero), allocations.Sum());
        // Every allocation must itself be a valid paisa amount (2 decimal places).
        Assert.All(allocations, a => Assert.Equal(a, Math.Round(a, 2)));
    }

    [Fact]
    public void AllocateAcrossDaysRoundedToCents_EvenlyDivisibleCase_MatchesUnroundedAllocation()
    {
        var charge = new FppasCharge(Guid.NewGuid(), sourceEnergyCharge: 6000m, rateFraction: -0.14m, notifiedAt: new DateTime(2026, 5, 28));

        var allocations = charge.AllocateAcrossDaysRoundedToCents(daysInBillingMonth: 30);

        Assert.All(allocations, a => Assert.Equal(-28.00m, a));
    }

    [Fact]
    public void Constructor_NegativeSourceEnergyCharge_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new FppasCharge(Guid.NewGuid(), sourceEnergyCharge: -1m, rateFraction: 0.05m, notifiedAt: DateTime.UtcNow));
    }

    [Fact]
    public void AllocateAcrossDays_NonPositiveDays_Throws()
    {
        var charge = new FppasCharge(Guid.NewGuid(), 1000m, 0.05m, DateTime.UtcNow);

        Assert.Throws<ArgumentOutOfRangeException>(() => charge.AllocateAcrossDays(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => charge.AllocateAcrossDaysRoundedToCents(0));
    }
}
