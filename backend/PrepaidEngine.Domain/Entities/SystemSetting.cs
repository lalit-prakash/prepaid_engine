namespace PrepaidEngine.Domain.Entities;

/// <summary>
/// A value an administrator has set from System Settings, overriding the default that comes from configuration. Only settings the engine
/// declares as editable are stored (and honoured). Removing a row puts the setting back to its default.
/// </summary>
public class SystemSetting
{
    public string Key { get; private set; } = string.Empty;
    public string Value { get; private set; } = string.Empty;
    public DateTime UpdatedAt { get; private set; }
    public string UpdatedBy { get; private set; } = string.Empty;

    public SystemSetting(string key, string value, DateTime updatedAt, string updatedBy)
    {
        if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("A key is required.", nameof(key));
        Key = key;
        Value = value;
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }

    // EF Core / serialization
    private SystemSetting() { }

    public void Change(string value, DateTime updatedAt, string updatedBy)
    {
        Value = value;
        UpdatedAt = updatedAt;
        UpdatedBy = updatedBy;
    }
}
