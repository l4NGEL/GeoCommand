using System.Globalization;

namespace GeoCommand.Infrastructure.DataSources.Replay;

public sealed record ReplayRow(int LineNumber, double OffsetSeconds, string Callsign, double Latitude, double Longitude, double SpeedMps, double HeadingDegrees);

/// <summary>
/// Kayıt dosyası biçimi (CSV, UTF-8, ondalık ayırıcı nokta):
/// <code>
/// # yorum satırları '#' ile başlar
/// offset_seconds,callsign,latitude,longitude,speed_mps,heading_deg
/// 0,ALFA-1,39.9208,32.8541,12.5,90
/// </code>
/// offset_seconds, oynatmanın başlangıcına göre saniyedir ve azalmamalıdır.
/// Değer aralıkları burada değil, tüm kaynaklar için ortak olan alma hattında doğrulanır.
/// </summary>
public static class ReplayFile
{
    public const string Header = "offset_seconds,callsign,latitude,longitude,speed_mps,heading_deg";

    public static IReadOnlyList<ReplayRow> Parse(TextReader reader, string sourceName)
    {
        var rows = new List<ReplayRow>();
        var errors = new List<string>();
        var headerSeen = false;
        var lineNumber = 0;

        while (reader.ReadLine() is { } raw)
        {
            lineNumber++;
            var line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;

            if (!headerSeen)
            {
                if (!string.Equals(line, Header, StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Satır {lineNumber}: başlık satırı '{Header}' olmalı.");
                    break;
                }
                headerSeen = true;
                continue;
            }

            var parts = line.Split(',');
            if (parts.Length != 6)
            {
                errors.Add($"Satır {lineNumber}: 6 alan bekleniyordu, {parts.Length} alan var.");
                continue;
            }

            var numbers = new double[5];
            var numberIndexes = new[] { 0, 2, 3, 4, 5 };
            var ok = true;
            for (var i = 0; i < numberIndexes.Length; i++)
            {
                if (!double.TryParse(parts[numberIndexes[i]].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out numbers[i]))
                {
                    errors.Add($"Satır {lineNumber}: '{parts[numberIndexes[i]].Trim()}' sayı değil (ondalık ayırıcı nokta olmalı).");
                    ok = false;
                    break;
                }
            }
            if (!ok) continue;

            var callsign = parts[1].Trim();
            if (callsign.Length == 0) { errors.Add($"Satır {lineNumber}: çağrı adı boş."); continue; }
            if (numbers[0] < 0) { errors.Add($"Satır {lineNumber}: offset_seconds negatif olamaz."); continue; }
            if (rows.Count > 0 && numbers[0] < rows[^1].OffsetSeconds)
            {
                errors.Add($"Satır {lineNumber}: offset_seconds bir önceki satırdan ({rows[^1].OffsetSeconds}) küçük olamaz.");
                continue;
            }

            rows.Add(new ReplayRow(lineNumber, numbers[0], callsign, numbers[1], numbers[2], numbers[3], numbers[4]));
        }

        if (!headerSeen && errors.Count == 0) errors.Add("Dosyada başlık satırı yok.");
        if (errors.Count == 0 && rows.Count == 0) errors.Add("Dosyada veri satırı yok.");
        if (errors.Count > 0)
        {
            var shown = errors.Take(10).ToList();
            if (errors.Count > shown.Count) shown.Add($"(+{errors.Count - shown.Count} hata daha)");
            throw new InvalidDataException($"{sourceName} okunamadı: {string.Join(" ", shown)}");
        }
        return rows;
    }
}
