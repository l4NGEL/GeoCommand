using System.IO.Ports;
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
        Assert.Equal(
            [new NmeaStream("ALFA-1", new TcpEndpoint("10.0.0.5", 10110)), new NmeaStream("BRAVO-2", new TcpEndpoint("10.0.0.6", 10110))],
            settings.Scenarios["SAHA"]);
    }

    [Fact]
    public void Serial_stream_defaults_to_nmea_0183_line_settings()
    {
        var settings = NmeaSettings.Read(Config(
            ("Scenarios:seri:0:Callsign", "ALFA-1"), ("Scenarios:seri:0:SerialPort", "COM5")));

        var stream = Assert.Single(settings.Scenarios["seri"]);
        Assert.Equal(new SerialEndpoint("COM5", 4800, Parity.None, 8, StopBits.One), stream.Endpoint);
        Assert.Equal("ALFA-1 ← COM5 (4800 8N1)", stream.ToString());
    }

    [Fact]
    public void Serial_and_tcp_streams_can_be_mixed_and_line_settings_overridden()
    {
        var settings = NmeaSettings.Read(Config(
            ("Scenarios:karma:0:Callsign", "ALFA-1"), ("Scenarios:karma:0:SerialPort", "/dev/ttyUSB0"),
            ("Scenarios:karma:0:BaudRate", "38400"), ("Scenarios:karma:0:Parity", "even"),
            ("Scenarios:karma:0:DataBits", "7"), ("Scenarios:karma:0:StopBits", "Two"),
            ("Scenarios:karma:1:Callsign", "BRAVO-2"), ("Scenarios:karma:1:Host", "127.0.0.1"), ("Scenarios:karma:1:Port", "10111")));

        var streams = settings.Scenarios["karma"];
        Assert.Equal(new SerialEndpoint("/dev/ttyUSB0", 38400, Parity.Even, 7, StopBits.Two), streams[0].Endpoint);
        Assert.Equal("/dev/ttyUSB0 (38400 7E2)", streams[0].Endpoint.ToString());
        Assert.IsType<TcpEndpoint>(streams[1].Endpoint);
    }

    [Theory]
    [InlineData("BaudRate", "9600x", "BaudRate")]
    [InlineData("BaudRate", "50", "BaudRate")]
    [InlineData("DataBits", "9", "DataBits")]
    [InlineData("Parity", "1", "Parity")]           // sayı: Enum.TryParse kabul ederdi
    [InlineData("Parity", "Hiçbiri", "Parity")]
    [InlineData("StopBits", "None", "StopBits")]    // SerialPort desteklemez
    [InlineData("StopBits", "One,Two", "StopBits")] // birleşim: Enum.TryParse OnePointFive'a çevirirdi
    [InlineData("StopBits", "", "StopBits")]
    public void Invalid_serial_line_settings_are_rejected(string key, string value, string expected)
    {
        var ex = Assert.Throws<InvalidDataException>(() => NmeaSettings.Read(Config(
            ("Scenarios:s:0:Callsign", "ALFA-1"), ("Scenarios:s:0:SerialPort", "COM5"), ($"Scenarios:s:0:{key}", value))));

        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void Stream_must_be_either_serial_or_tcp_and_a_serial_port_cannot_be_shared()
    {
        var ex = Assert.Throws<InvalidDataException>(() => NmeaSettings.Read(Config(
            ("Scenarios:a:0:Callsign", "ALFA-1"), ("Scenarios:a:0:SerialPort", "COM5"), ("Scenarios:a:0:Host", "127.0.0.1"),
            ("Scenarios:b:0:Callsign", "ALFA-1"), ("Scenarios:b:0:SerialPort", "COM5"),
            ("Scenarios:b:1:Callsign", "BRAVO-2"), ("Scenarios:b:1:SerialPort", "com5"))));

        Assert.Contains("Nmea:Scenarios:a:0: SerialPort ile Host/Port birlikte kullanılamaz.", ex.Message);
        Assert.Contains("Nmea:Scenarios:b: aynı seri port birden çok akışta: COM5.", ex.Message);
    }

    [Fact]
    public void All_configuration_errors_are_reported_together()
    {
        var ex = Assert.Throws<InvalidDataException>(() => NmeaSettings.Read(Config(
            ("ReconnectSeconds", "0"),
            ("Scenarios:a:0:Callsign", "ALFA-1"), ("Scenarios:a:0:Port", "70000"),
            ("Scenarios:a:1:Callsign", "alfa-1"), ("Scenarios:a:1:Host", "h"), ("Scenarios:a:1:Port", "1"))));

        Assert.Contains("Host veya SerialPort zorunlu", ex.Message);
        Assert.Contains("Port 1-65535", ex.Message);
        Assert.Contains("yinelenen çağrı adları", ex.Message); // geçersiz uç noktalı akış da çakışma denetimine girer
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
