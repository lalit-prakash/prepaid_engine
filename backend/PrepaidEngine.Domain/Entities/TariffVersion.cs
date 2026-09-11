namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// An immutable record of a single parameter change made to a <see cref="Tariff"/> over time —
/// what changed, from what to what, and when it took effect. Enables a "Tariff Change Report"
/// even though <see cref="Tariff"/> itself still only exposes its single current version (see
/// README's Tariff Management section on why there is no tariff update endpoint yet): a version
/// is recorded alongside a change, not derived retroactively, so this is additive history, not a
/// substitute for the still-missing versioned-tariff-editing workflow.
/// </summary>
public sealed class TariffVersion
{
    public Guid Id { get; }
    public Guid TariffId { get; }
    public string FieldName { get; }
    public string OldValue { get; }
    public string NewValue { get; }
    public string ChangeNote { get; }
    public DateTime EffectiveDate { get; }
    public DateTime RecordedAt { get; }

    public TariffVersion(
        Guid id,
        Guid tariffId,
        string fieldName,
        string oldValue,
        string newValue,
        string changeNote,
        DateTime effectiveDate,
        DateTime recordedAt)
    {
        if (string.IsNullOrWhiteSpace(fieldName))
            throw new ArgumentException("Field name is required.", nameof(fieldName));
        if (string.IsNullOrWhiteSpace(changeNote))
            throw new ArgumentException("A change note is required to record a tariff version.", nameof(changeNote));

        Id = id;
        TariffId = tariffId;
        FieldName = fieldName;
        OldValue = oldValue ?? string.Empty;
        NewValue = newValue ?? string.Empty;
        ChangeNote = changeNote;
        EffectiveDate = effectiveDate;
        RecordedAt = recordedAt;
    }

    // EF Core / serialization
    private TariffVersion()
    {
        FieldName = string.Empty;
        OldValue = string.Empty;
        NewValue = string.Empty;
        ChangeNote = string.Empty;
    }
}
