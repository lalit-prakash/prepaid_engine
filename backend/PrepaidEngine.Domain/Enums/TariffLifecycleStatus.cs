namespace PrepaidEngine.Domain.Enums;

/// <summary>Whether a <see cref="Entities.Tariff"/> row is the one currently billed against
/// (<c>Active</c>) or has been superseded by a later <see cref="Entities.TariffChangeRequest"/>
/// (<c>Retired</c>). A <c>Tariff</c> row is never mutated once created (see the entity's own doc
/// comment) — only this status flips, at the superseding request's commencement date — so every
/// historical bill's <c>TariffId</c> keeps pointing at the exact rates it was actually billed
/// under, with zero changes required to the billing calculation path itself.</summary>
public enum TariffLifecycleStatus
{
    Active = 0,
    Retired = 1,
}
