namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A slab-based tariff plan used to compute bill amounts from metered consumption.
/// </summary>
public class Tariff
{
    private readonly List<TariffSlab> _slabs = new();

    public Guid Id { get; private set; }
    public string Name { get; private set; }
    public IReadOnlyCollection<TariffSlab> Slabs => _slabs.AsReadOnly();

    public Tariff(Guid id, string name, IEnumerable<TariffSlab> slabs)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));

        Id = id;
        Name = name;
        _slabs.AddRange(slabs ?? throw new ArgumentNullException(nameof(slabs)));

        if (_slabs.Count == 0)
            throw new ArgumentException("A tariff must define at least one slab.", nameof(slabs));
    }

    // EF Core / serialization
    private Tariff()
    {
        Name = string.Empty;
    }

    /// <summary>
    /// Computes the total charge for the given consumption by summing each slab's contribution.
    /// </summary>
    public decimal CalculateAmount(decimal consumptionKwh)
    {
        if (consumptionKwh < 0)
            throw new ArgumentOutOfRangeException(nameof(consumptionKwh));

        return _slabs.Sum(slab => slab.KwhWithinSlab(consumptionKwh) * slab.RatePerKwh);
    }
}
