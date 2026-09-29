using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace GeoCommand.Plugins.Nmea;

/// <summary>Bir aracın NMEA akışı: her alıcı (GPS) tek araca aittir, çağrı adı yapılandırmadan gelir.</summary>
public sealed record NmeaStream(string Callsign, string Host, int Port)
{
    public override string ToString() => $"{Callsign} ← {Host}:{Port}";
}

/// <summary>
/// <c>DataSource:Nmea</c> yapılandırması. Örnek:
/// <code>
/// "Nmea": {
///   "DefaultScenario": "yerel-alici",
///   "ReconnectSeconds": 5,
///   "Scenarios": {
///     "yerel-alici": [ { "Callsign": "ALFA-1", "Host": "127.0.0.1", "Port": 10110 } ]
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
            foreach (var item in scenario.GetChildren())
            {
                var prefix = $"Nmea:Scenarios:{scenario.Key}:{item.Key}: ";
                var callsign = item["Callsign"]?.Trim() ?? "";
                var host = item["Host"]?.Trim() ?? "";
                if (callsign.Length == 0) errors.Add(prefix + "Callsign zorunlu.");
                if (host.Length == 0) errors.Add(prefix + "Host zorunlu.");
                if (!int.TryParse(item["Port"], NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
                    errors.Add(prefix + "Port 1-65535 arasında olmalı.");
                streams.Add(new NmeaStream(callsign, host, port));
            }

            if (streams.Count == 0) errors.Add($"Nmea:Scenarios:{scenario.Key}: en az bir akış tanımlanmalı.");
            var duplicates = streams.GroupBy(s => s.Callsign, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (duplicates.Count > 0) errors.Add($"Nmea:Scenarios:{scenario.Key}: yinelenen çağrı adları: {string.Join(", ", duplicates)}.");
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
}
