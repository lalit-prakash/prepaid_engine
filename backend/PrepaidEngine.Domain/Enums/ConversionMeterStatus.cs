namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// The meter's health status as RMS reports it at conversion time. The integration requirement
/// text names this field ("Meter Status") without enumerating its allowed values — this project
/// picks the minimal two-value set a conversion decision actually needs (a faulty meter is a
/// reasonable ground to still record/audit the request, even though this project does not yet
/// reject a conversion on this basis alone) rather than inventing a larger vocabulary nothing
/// downstream would use.
/// </summary>
public enum ConversionMeterStatus
{
    Normal,
    Faulty,
}
