namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// The two business roles the tariff-governance workflow distinguishes (see
/// <see cref="Entities.TariffChangeRequest"/>'s doc comment). IT drafts/edits/submits a proposed
/// tariff change; Utility reviews and approves/rejects it. A single actor is never both for the
/// same change — enforced primarily by role-gated endpoints (an IT-credentialed caller cannot
/// reach the approve/reject endpoints at all), with a same-actor check as defense in depth.
/// </summary>
public enum UserRole
{
    IT = 0,
    Utility = 1,
}
