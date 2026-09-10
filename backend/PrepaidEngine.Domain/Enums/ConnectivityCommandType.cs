namespace PrepaidEngine.Domain.Enums;

/// <summary>
/// Which direction a <see cref="Entities.ConnectivityCommand"/> is driving the consumer's
/// supply connection. Kept as an explicit type rather than inferring it from context, since the
/// UI/UX request this project follows is emphatic that "reverse" is a real, distinct failure
/// mode (a Postpaid→Prepaid conversion accidentally treated as Prepaid→Postpaid, or a Reconnect
/// command mistakenly sent when Disconnect was intended) — the direction must always be an
/// explicit, unambiguous fact on the record, not derived.
/// </summary>
public enum ConnectivityCommandType
{
    Disconnect,
    Reconnect,
}
