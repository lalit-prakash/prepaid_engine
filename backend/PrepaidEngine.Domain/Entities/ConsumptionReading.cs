namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A single consumption reading (delta, in kWh) captured from a consumer's smart meter,
/// used as input to bill generation.
/// </summary>
public class ConsumptionReading
{
    public Guid Id { get; private set; }
    public Guid ConsumerId { get; private set; }
    public decimal ConsumptionKwh { get; private set; }
    public DateTime PeriodStart { get; private set; }
    public DateTime PeriodEnd { get; private set; }

    public ConsumptionReading(Guid id, Guid consumerId, decimal consumptionKwh, DateTime periodStart, DateTime periodEnd)
    {
        if (consumptionKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(consumptionKwh), "Consumption cannot be negative.");
        if (periodEnd < periodStart)
            throw new ArgumentException("Period end cannot be before period start.", nameof(periodEnd));

        Id = id;
        ConsumerId = consumerId;
        ConsumptionKwh = consumptionKwh;
        PeriodStart = periodStart;
        PeriodEnd = periodEnd;
    }

    // EF Core / serialization
    private ConsumptionReading()
    {
    }
}
