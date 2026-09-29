using System.Globalization;

namespace GeoCommand.Plugins.Nmea;

/// <summary>Bir <c>$xxRMC</c> cümlesinden çıkarılan konum. Rota bilgisi (COG) duran alıcılarda boş gelebilir.</summary>
public sealed record RmcFix(DateTimeOffset TimestampUtc, double Latitude, double Longitude, double SpeedMps, double? CourseDegrees);

public enum RmcParseStatus
{
    Ok,
    /// <summary>RMC değil (GGA, GSV vb.); sessizce atlanır.</summary>
    NotRmc,
    /// <summary>Alıcı geçerli konum bildirmiyor (durum alanı 'V').</summary>
    NoFix,
    Invalid
}

/// <summary>
/// NMEA 0183 <c>RMC</c> (Recommended Minimum) cümle ayrıştırıcısı. Örnek:
/// <code>$GPRMC,123519.50,A,3955.2480,N,03251.2460,E,24.3,152.9,280926,,,A*6A</code>
/// Alanlar: saat, durum (A/V), enlem ddmm.mmmm + N/S, boylam dddmm.mmmm + E/W, hız (knot), rota (derece),
/// tarih ddmmyy, manyetik sapma, (NMEA 2.3+) mod. Konuşucu kimliği (GP, GN, GL, GA …) önemsenmez.
/// Sağlama toplamı zorunludur: bozuk seri hat veya TCP verisi ağırlıklı olarak burada elenir.
/// </summary>
public static class RmcSentence
{
    public const double MetersPerSecondPerKnot = 1852.0 / 3600.0;

    /// <summary>NMEA 0183'e göre bir cümle en fazla 82 karakterdir; biraz pay bırakılır.</summary>
    public const int MaxLength = 120;

    public static RmcParseStatus TryParse(string line, out RmcFix? fix, out string? error)
    {
        fix = null;
        error = null;
        line = line.Trim();

        if (line.Length > MaxLength) return Fail("cümle çok uzun", out error);
        if (line.Length < 7 || line[0] != '$') return Fail("'$' ile başlamıyor", out error);

        var star = line.LastIndexOf('*');
        if (star < 0 || star != line.Length - 3) return Fail("sağlama toplamı yok", out error);
        if (!byte.TryParse(line.AsSpan(star + 1, 2), NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var expected))
            return Fail("sağlama toplamı onaltılık değil", out error);
        var actual = Checksum(line.AsSpan(1, star - 1));
        if (actual != expected) return Fail($"sağlama toplamı hatalı (beklenen {expected:X2}, hesaplanan {actual:X2})", out error);

        var fields = line[1..star].Split(',');
        if (fields[0].Length != 5 || !fields[0].EndsWith("RMC", StringComparison.Ordinal)) return RmcParseStatus.NotRmc;
        if (fields.Length < 10) return Fail($"RMC en az 10 alan içermeli, {fields.Length} var", out error);

        if (fields[2] == "V") return RmcParseStatus.NoFix;
        if (fields[2] != "A") return Fail($"durum alanı '{fields[2]}' (A veya V olmalı)", out error);

        if (!TryParseTime(fields[1], fields[9], out var timestamp)) return Fail("saat/tarih alanı geçersiz", out error);
        if (!TryParseCoordinate(fields[3], fields[4], 2, 'N', 'S', 90, out var latitude)) return Fail("enlem geçersiz", out error);
        if (!TryParseCoordinate(fields[5], fields[6], 3, 'E', 'W', 180, out var longitude)) return Fail("boylam geçersiz", out error);

        if (!double.TryParse(fields[7], NumberStyles.Float, CultureInfo.InvariantCulture, out var knots) || knots < 0)
            return Fail("hız geçersiz", out error);

        double? course = null;
        if (fields[8].Length > 0)
        {
            if (!double.TryParse(fields[8], NumberStyles.Float, CultureInfo.InvariantCulture, out var c) || c is < 0 or > 360)
                return Fail("rota geçersiz", out error);
            course = c % 360;
        }

        fix = new RmcFix(timestamp, latitude, longitude, knots * MetersPerSecondPerKnot, course);
        return RmcParseStatus.Ok;
    }

    /// <summary><see cref="TryParse"/>'ın tersi: <c>$GPRMC</c> cümlesi üretir (sonunda CR/LF yok). Testler ve yayıncılar için.</summary>
    public static string Format(RmcFix fix, string talker = "GP")
    {
        static string DegreesMinutes(double value, int degreeDigits)
        {
            value = Math.Abs(value);
            var degrees = (int)value;
            var minutes = Math.Round((value - degrees) * 60, 4);
            if (minutes >= 60) { degrees++; minutes = 0; }
            return degrees.ToString(new string('0', degreeDigits), CultureInfo.InvariantCulture) +
                   minutes.ToString("00.0000", CultureInfo.InvariantCulture);
        }

        var t = fix.TimestampUtc.UtcDateTime;
        var body = string.Join(',',
            talker + "RMC",
            t.ToString("HHmmss.ff", CultureInfo.InvariantCulture),
            "A",
            DegreesMinutes(fix.Latitude, 2), fix.Latitude < 0 ? "S" : "N",
            DegreesMinutes(fix.Longitude, 3), fix.Longitude < 0 ? "W" : "E",
            (fix.SpeedMps / MetersPerSecondPerKnot).ToString("0.0", CultureInfo.InvariantCulture),
            fix.CourseDegrees?.ToString("0.0", CultureInfo.InvariantCulture) ?? "",
            t.ToString("ddMMyy", CultureInfo.InvariantCulture),
            "", "", "A");
        return $"${body}*{Checksum(body):X2}";
    }

    /// <summary>'$' ile '*' arasındaki tüm karakterlerin XOR'u.</summary>
    public static byte Checksum(ReadOnlySpan<char> body)
    {
        byte sum = 0;
        foreach (var ch in body) sum ^= (byte)ch;
        return sum;
    }

    private static RmcParseStatus Fail(string message, out string? error)
    {
        error = message;
        return RmcParseStatus.Invalid;
    }

    /// <summary>hhmmss[.sss] + ddmmyy. İki haneli yıl 2000'li yıllar olarak yorumlanır.</summary>
    private static bool TryParseTime(string time, string date, out DateTimeOffset timestamp)
    {
        timestamp = default;
        if (time.Length < 6 || date.Length != 6) return false;
        if (!int.TryParse(time.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var hh) ||
            !int.TryParse(time.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var mm) ||
            // decimal: double ile "05.06" saniyesi 05.0599999 olur ve zaman damgası kayar.
            !decimal.TryParse(time.AsSpan(4), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var ss) ||
            !int.TryParse(date.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var day) ||
            !int.TryParse(date.AsSpan(2, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var month) ||
            !int.TryParse(date.AsSpan(4, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var yy))
            return false;
        if (hh > 23 || mm > 59 || ss >= 60 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(2000 + yy, month))
            return false;

        timestamp = new DateTimeOffset(2000 + yy, month, day, hh, mm, 0, TimeSpan.Zero).AddTicks((long)(ss * TimeSpan.TicksPerSecond));
        return true;
    }

    /// <summary>(d)ddmm.mmmm biçimi: ilk <paramref name="degreeDigits"/> hane derece, kalanı dakika.</summary>
    private static bool TryParseCoordinate(string value, string hemisphere, int degreeDigits, char positive, char negative, double max, out double result)
    {
        result = 0;
        if (value.Length < degreeDigits + 2 || hemisphere.Length != 1) return false;
        if (!int.TryParse(value.AsSpan(0, degreeDigits), NumberStyles.None, CultureInfo.InvariantCulture, out var degrees) ||
            !double.TryParse(value.AsSpan(degreeDigits), NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var minutes) ||
            minutes >= 60)
            return false;

        result = degrees + minutes / 60.0;
        if (result > max) return false;
        if (hemisphere[0] == negative) result = -result;
        else if (hemisphere[0] != positive) return false;
        return true;
    }
}
