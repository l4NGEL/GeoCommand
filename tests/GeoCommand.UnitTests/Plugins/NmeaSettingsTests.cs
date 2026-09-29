using GeoCommand.Plugins.Nmea;
using Microsoft.Extensions.Configuration;

namespace GeoCommand.UnitTests.Plugins;

public class NmeaSettingsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    [Fact]
    public void Reads_scenarios_streams_and_reconnect_delay()
    {
        var settings = NmeaSettings.Read(Config(
            ("ReconnectSeconds", "2.5"),
            ("Scenarios:saha:0:Callsign", "ALFA-1"), ("Scenarios:saha:0:Host", "10.0.0.5"), ("Scenarios:saha:0:Port", "10110"),
            ("Scenarios:saha:1:Callsign", "BRAVO-2"), ("Scenarios:saha:1:Host", "10.0.0.6"), ("Scenarios:saha:1:Port", "10110")));

        Assert.Equal("saha", settings.DefaultScenario); // belirtilmezse ilk senaryo
        Assert.Equal(TimeSpan.FromSeconds(2.5), settings.ReconnectDelay);
        Assert.Equal([new NmeaStream("ALFA-1", "10.0.0.5", 10110), new NmeaStream("BRAVO-2", "10.0.0.6", 10110)], settings.Scenarios["SAHA"]);
    }

    [Fact]
    public void All_configuration_errors_are_reported_together()
    {
        var ex = Assert.Throws<InvalidDataException>(() => NmeaSettings.Read(Config(
            ("ReconnectSeconds", "0"),
            ("Scenarios:a:0:Callsign", "ALFA-1"), ("Scenarios:a:0:Port", "70000"),
            ("Scenarios:a:1:Callsign", "alfa-1"), ("Scenarios:a:1:Host", "h"), ("Scenarios:a:1:Port", "1"))));

        Assert.Contains("Host zorunlu", ex.Message);
        Assert.Contains("Port 1-65535", ex.Message);
        Assert.Contains("yinelenen çağrı adları", ex.Message);
        Assert.Contains("ReconnectSeconds", ex.Message);
    }

    [Fact]
    public void Missing_section_means_no_scenarios_rather_than_an_error()
    {
        var settings = NmeaSettings.Read(Config());

        Assert.Empty(settings.Scenarios);
        Assert.Equal("", settings.DefaultScenario);
    }
}
