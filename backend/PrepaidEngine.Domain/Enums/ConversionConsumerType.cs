namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Consumer type as pushed by RMS on a prepaid conversion request (AMISP integration
/// requirement doc, section 1: "Consumer type(VIP/Hospitals/schools/shopping complex etc)").
/// Deliberately a distinct enum from <see cref="ConsumerCategory"/> — that one is the
/// regulatory tariff-filing category (Domestic/Industrial/etc.), an entirely different
/// classification RMS does not send on a conversion request; conflating the two would silently
/// corrupt whichever one lost the name collision.
/// </summary>
public enum ConversionConsumerType
{
    Residential,
    Vip,
    Hospital,
    School,
    ShoppingComplex,
    Other,
}
