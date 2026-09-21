using Microsoft.Extensions.Configuration;
using PrepaidEngine.Api.Settings;

namespace PrepaidEngine.Tests.Settings;

public class SettingsRegistryTests
{
    private static SettingDefinition Def(string key) => SettingsRegistry.Find(key)!;

    [Theory]
    [InlineData("LowBalance:ThresholdRs", "150.50")]
    [InlineData("LowBalance:ThresholdRs", "0")]
    [InlineData("LowBalance:ThresholdRs", "")]            // blank = use the default behaviour
    [InlineData("SlaMonitoring:MeterCreditTargetMinutes", "15")]
    [InlineData("SlaMonitoring:MeterCreditTargetMinutes", "")]        // blank = back to the default
    [InlineData("EnergyValidation:WarningTolerancePct", "2.5")]
    public void Accepts_good_values(string key, string value) => Assert.Null(SettingsRegistry.Validate(Def(key), value));

    [Theory]
    [InlineData("LowBalance:ThresholdRs", "-1")]
    [InlineData("LowBalance:ThresholdRs", "abc")]
    [InlineData("SlaMonitoring:MeterCreditTargetMinutes", "0")]
    [InlineData("SlaMonitoring:MeterCreditTargetMinutes", "5.5")]      // whole numbers only
    [InlineData("EnergyValidation:FailTolerancePct", "101")]
    public void Rejects_bad_values(string key, string value) => Assert.NotNull(SettingsRegistry.Validate(Def(key), value));

    [Fact]
    public void Only_declared_settings_are_editable()
    {
        Assert.True(SettingsRegistry.IsEditable("LowBalance:ThresholdRs"));
        Assert.False(SettingsRegistry.IsEditable("Jwt:Key"));
        Assert.False(SettingsRegistry.IsEditable("ConnectionStrings:PrepaidEngine"));
    }

    [Fact]
    public void Normalise_gives_invariant_text_without_trailing_zeros()
    {
        Assert.Equal("150.5", SettingsRegistry.Normalise(" 150.50 "));
        Assert.Equal("15", SettingsRegistry.Normalise("15"));
    }

    [Fact]
    public void Every_key_is_unique_and_has_a_sensible_range()
    {
        Assert.Equal(SettingsRegistry.All.Length, SettingsRegistry.All.Select(d => d.Key).Distinct().Count());
        Assert.All(SettingsRegistry.All, d => Assert.True(d.Min < d.Max));
    }

    [Fact]
    public void The_database_source_contributes_nothing_when_the_database_is_unreachable()
    {
        var provider = new DbSettingsConfigurationSource("Host=127.0.0.1;Port=1;Database=none;Username=x;Password=y;Timeout=1").Build(new ConfigurationBuilder());
        provider.Load();
        Assert.False(provider.TryGet("LowBalance:ThresholdRs", out _));
    }
}
