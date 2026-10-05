using System.Globalization;
using System.IO.Ports;
using Microsoft.Extensions.Configuration;

namespace GeoCommand.Plugins.Nmea;

/// <summary>NMEA cümlelerinin okunduğu fiziksel/mantıksal hat.</summary>
public abstract record NmeaEndpoint;

/// <summary>NMEA-over-TCP ağ geçidi (10110 standart porttur).</summary>
public sealed record TcpEndpoint(string Host, int Port) : NmeaEndpoint
{
    public override string ToString() => $"{Host}:{Port}";
}

/// <summary>RS-232 / USB-seri alıcı. NMEA 0183 standardı 4800 baud 8N1'dir; yüksek hızlı alıcılar 38400 kullanır.</summary>
public sealed record SerialEndpoint(string PortName, int BaudRate, Parity Parity, int DataBits, StopBits StopBits) : NmeaEndpoint
{
    public const int DefaultBaudRate = 4800;

    public override string ToString()
    {
        var parity = Parity switch { Parity.None => 'N', Parity.Even => 'E', Parity.Odd => 'O', Parity.Mark => 'M', _ => 'S' };
        var stop = StopBits switch { StopBits.One => "1", StopBits.OnePointFive => "1.5", _ => "2" };
        return $"{PortName} ({BaudRate} {DataBits}{parity}{stop})";
    }
}

/// <summary>Bir aracın NMEA akışı: her alıcı (GPS) tek araca aittir, çağrı adı yapılandırmadan gelir.</summary>
public sealed record NmeaStream(string Callsign, NmeaEndpoint Endpoint)
{
    public override string ToString() => $"{Callsign} ← {Endpoint}";
}

/// <summary>
/// <c>DataSource:Nmea</c> yapılandırması. Her akış ya TCP (<c>Host</c> + <c>Port</c>) ya da seri port
/// (<c>SerialPort</c>, isteğe bağlı <c>BaudRate</c>/<c>Parity</c>/<c>DataBits</c>/<c>StopBits</c>) tanımlar. Örnek:
/// <code>
/// "Nmea": {
///   "DefaultScenario": "yerel-alici",
///   "ReconnectSeconds": 5,
///   "Scenarios": {
///     "yerel-alici": [ { "Callsign": "ALFA-1", "Host": "127.0.0.1", "Port": 10110 } ],
///     "seri-alici":  [ { "Callsign": "ALFA-1", "SerialPort": "COM5", "BaudRate": 4800 } ]
///   }
/// }
/// </code>
/// Binder paketine bağımlı olmamak için elle okunur (plugin yalnızca GeoCommand.Sdk'ya bağlıdır).
/// </summary>
public sealed class NmeaSettings
{
    public string DefaultScenario { get; private init; } = "";
    public TimeSpan ReconnectDelay { get; private init; } = TimeSpan.FromSeconds(5);
    public IReadOnlyDictionary<string, IReadOnlyList<NmeaStream>> Scenarios { get; private init; } =
        new Dictionary<string, IReadOnlyList<NmeaStream>>();

    public static NmeaSettings Read(IConfiguration section)
    {
        var errors = new List<string>();
        var scenarios = new Dictionary<string, IReadOnlyList<NmeaStream>>(StringComparer.OrdinalIgnoreCase);

        foreach (var scenario in section.GetSection("Scenarios").GetChildren())
        {
            var streams = new List<NmeaStream>();
            var callsigns = new List<string>();
            foreach (var item in scenario.GetChildren())
            {
                var prefix = $"Nmea:Scenarios:{scenario.Key}:{item.Key}: ";
                var callsign = item["Callsign"]?.Trim() ?? "";
                if (callsign.Length == 0) errors.Add(prefix + "Callsign zorunlu.");
                else callsigns.Add(callsign);

                var serial = item["SerialPort"]?.Trim() ?? "";
                var host = item["Host"]?.Trim() ?? "";
                NmeaEndpoint? endpoint;
                if (serial.Length > 0 && (host.Length > 0 || item["Port"] is not null))
                {
                    errors.Add(prefix + "SerialPort ile Host/Port birlikte kullanılamaz.");
                    endpoint = null;
                }
                else
                {
                    endpoint = serial.Length > 0 ? ReadSerial(item, serial, prefix, errors) : ReadTcp(item, host, prefix, errors);
                }
                if (endpoint is not null) streams.Add(new NmeaStream(callsign, endpoint));
            }

            if (!scenario.GetChildren().Any()) errors.Add($"Nmea:Scenarios:{scenario.Key}: en az bir akış tanımlanmalı.");
            var duplicates = callsigns.GroupBy(c => c, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicates.Count > 0) errors.Add($"Nmea:Scenarios:{scenario.Key}: yinelenen çağrı adları: {string.Join(", ", duplicates)}.");
            // Bir seri port aynı anda tek süreç/tek okuyucu tarafından açılabilir.
            var sharedPorts = streams.Select(s => s.Endpoint).OfType<SerialEndpoint>()
                .GroupBy(e => e.PortName, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (sharedPorts.Count > 0) errors.Add($"Nmea:Scenarios:{scenario.Key}: aynı seri port birden çok akışta: {string.Join(", ", sharedPorts)}.");
            scenarios[scenario.Key] = streams;
        }

        var reconnect = 5.0;
        if (section["ReconnectSeconds"] is { } r &&
            (!double.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out reconnect) || reconnect is < 0.1 or > 300))
            errors.Add("Nmea:ReconnectSeconds 0.1-300 arasında olmalı.");

        if (errors.Count > 0) throw new InvalidDataException(string.Join(" ", errors));

        return new NmeaSettings
        {
            DefaultScenario = section["DefaultScenario"] ?? scenarios.Keys.Order().FirstOrDefault() ?? "",
            ReconnectDelay = TimeSpan.FromSeconds(reconnect),
            Scenarios = scenarios
        };
    }

    private static TcpEndpoint? ReadTcp(IConfigurationSection item, string host, string prefix, List<string> errors)
    {
        var ok = true;
        if (host.Length == 0) { errors.Add(prefix + "Host veya SerialPort zorunlu."); ok = false; }
        if (!int.TryParse(item["Port"], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
        {
            errors.Add(prefix + "Port 1-65535 arasında olmalı.");
            ok = false;
        }
        return ok ? new TcpEndpoint(host, port) : null;
    }

    private static SerialEndpoint? ReadSerial(IConfigurationSection item, string portName, string prefix, List<string> errors)
    {
        var count = errors.Count;

        var baudRate = SerialEndpoint.DefaultBaudRate;
        if (item["BaudRate"] is { } b &&
            (!int.TryParse(b, NumberStyles.None, CultureInfo.InvariantCulture, out baudRate) || baudRate is < 110 or > 921600))
            errors.Add(prefix + "BaudRate 110-921600 arasında olmalı.");

        var dataBits = 8;
        if (item["DataBits"] is { } d &&
            (!int.TryParse(d, NumberStyles.None, CultureInfo.InvariantCulture, out dataBits) || dataBits is < 5 or > 8))
            errors.Add(prefix + "DataBits 5-8 arasında olmalı.");

        var parity = Parity.None;
        if (item["Parity"] is { } p && !TryParseName(p, out parity))
            errors.Add(prefix + $"Parity şunlardan biri olmalı: {string.Join(", ", Enum.GetNames<Parity>())}.");

        var stopBits = StopBits.One;
        // StopBits.None SerialPort tarafından desteklenmez.
        if (item["StopBits"] is { } s && (!TryParseName(s, out stopBits) || stopBits == StopBits.None))
            errors.Add(prefix + "StopBits şunlardan biri olmalı: One, OnePointFive, Two.");

        return errors.Count == count ? new SerialEndpoint(portName, baudRate, parity, dataBits, stopBits) : null;
    }

    /// <summary>
    /// Yalnızca tek bir ad kabul edilir: "1" gibi sayılar veya "One,Two" gibi birleşimler <see cref="Enum.TryParse{TEnum}(string, bool, out TEnum)"/>
    /// tarafından sessizce başka bir değere çevrilmesin.
    /// </summary>
    private static bool TryParseName<T>(string value, out T result) where T : struct, Enum
    {
        result = default;
        var name = value.Trim();
        return name.Length > 0 && name.All(char.IsAsciiLetter) &&
               Enum.TryParse(name, ignoreCase: true, out result) && Enum.IsDefined(result);
    }
}
