namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Sign-in roles. IT and Utility are the two the tariff-governance workflow distinguishes (see
/// <see cref="Entities.TariffChangeRequest"/>'s doc comment). IT drafts/edits/submits a proposed
/// tariff change; Utility reviews and approves/rejects it. A single actor is never both for the
/// same change — enforced primarily by role-gated endpoints (an IT-credentialed caller cannot
/// reach the approve/reject endpoints at all), with a same-actor check as defense in depth.
/// </summary>
public enum UserRole
{
    IT = 0,
    Utility = 1,

    /// <summary>Full operational and data access plus tariff drafting; cannot approve tariffs (that stays Utility-only).</summary>
    Admin = 2,

    /// <summary>Day-to-day operations (recharge, disconnect/reconnect, retries, exceptions); no tariff or bulk-data changes.</summary>
    Operator = 3,

    /// <summary>Read-only: can view every screen, cannot change anything.</summary>
    ReadOnly = 4,
}
