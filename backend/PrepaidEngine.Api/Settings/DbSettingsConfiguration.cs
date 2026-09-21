using Microsoft.Extensions.Configuration;
using Npgsql;

namespace PrepaidEngine.Api.Settings;

/// <summary>
/// Adds the values saved in System Settings to the application's configuration, on top of appsettings, user-secrets and environment
/// variables, so the existing options classes pick them up without any other change. Only keys in <see cref="SettingsRegistry"/> are
/// honoured. The provider reads the <c>SystemSettings</c> table directly; when the table or the database is not there yet (before the first
/// migration) it simply contributes nothing, so start-up never depends on it. <see cref="Reload"/> is called after a save, which makes options
/// read through <c>IOptionsSnapshot</c> see the new values on their next request, with no restart.
/// </summary>
public sealed class DbSettingsConfigurationSource : IConfigurationSource
{
    private readonly string? _connectionString;

    public DbSettingsConfigurationSource(string? connectionString) => _connectionString = connectionString;

    public IConfigurationProvider Build(IConfigurationBuilder builder) => new DbSettingsConfigurationProvider(_connectionString);
}

public sealed class DbSettingsConfigurationProvider : ConfigurationProvider
{
    private readonly string? _connectionString;

    public DbSettingsConfigurationProvider(string? connectionString) => _connectionString = connectionString;

    public override void Load() => Data = Read();

    /// <summary>Re-reads the table and tells everything bound to the configuration.</summary>
    public void Reload()
    {
        Data = Read();
        OnReload();
    }

    private Dictionary<string, string?> Read()
    {
        var data = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(_connectionString)) return data;
        try
        {
            using var connection = new NpgsqlConnection(_connectionString);
            connection.Open();
            using var command = new NpgsqlCommand("SELECT \"Key\", \"Value\" FROM \"SystemSettings\"", connection);
            using var reader = command.ExecuteReader();
            while (reader.Read())
            {
                var key = reader.GetString(0);
                if (SettingsRegistry.IsEditable(key)) data[key] = reader.GetString(1);
            }
        }
        catch (Exception)
        {
            // No database yet, or no table yet: nothing has been saved, so there is nothing to add.
        }
        return data;
    }
}

/// <summary>Holds the provider once the host is built, so a save can reload it.</summary>
public sealed class SettingsReloader
{
    private readonly IConfigurationRoot _root;

    public SettingsReloader(IConfiguration configuration) => _root = (IConfigurationRoot)configuration;

    private DbSettingsConfigurationProvider? Provider => _root.Providers.OfType<DbSettingsConfigurationProvider>().FirstOrDefault();

    public void Reload() => Provider?.Reload();

    /// <summary>The value of a key ignoring what was saved in System Settings: what it would be by default.</summary>
    public string? DefaultValue(string key)
    {
        foreach (var p in _root.Providers.Reverse())
        {
            if (p is DbSettingsConfigurationProvider) continue;
            if (p.TryGet(key, out var v)) return v;
        }
        return null;
    }
}
