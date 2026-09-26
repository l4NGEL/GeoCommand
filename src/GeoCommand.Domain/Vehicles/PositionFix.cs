using GeoCommand.Domain.Common;

namespace GeoCommand.Domain.Vehicles;

/// <summary>Bir araçtan gelen, doğrulanmış tek bir konum bildirimi.</summary>
public sealed record PositionFix
{
    /// <summary>Simülasyondaki tüm araç tipleri için hız üst sınırı (~1.000 km/sa).</summary>
    public const double MaxSpeedMps = 280;

    /// <summary>Saat farkları için gelecekteki zaman damgalarına tanınan tolerans.</summary>
    public static readonly TimeSpan MaxClockSkew = TimeSpan.FromSeconds(5);

    public double Latitude { get; }
    public double Longitude { get; }
    public double SpeedMps { get; }
    public double HeadingDegrees { get; }
    public DateTimeOffset TimestampUtc { get; }

    private PositionFix(double latitude, double longitude, double speedMps, double headingDegrees, DateTimeOffset timestampUtc)
    {
        Latitude = latitude;
        Longitude = longitude;
        SpeedMps = speedMps;
        HeadingDegrees = headingDegrees;
        TimestampUtc = timestampUtc;
    }

    public static PositionFix Create(
        double latitude, double longitude, double speedMps, double headingDegrees,
        DateTimeOffset timestamp, DateTimeOffset now)
    {
        var errors = new List<string>();
        Geo.AddCoordinateErrors(errors, latitude, longitude);
        if (!double.IsFinite(speedMps) || speedMps < 0 || speedMps > MaxSpeedMps)
            errors.Add($"Hız 0 ile {MaxSpeedMps} m/s arasında olmalı (gelen: {speedMps}).");
        if (!double.IsFinite(headingDegrees) || headingDegrees < 0 || headingDegrees >= 360)
            errors.Add($"Yön 0 (dahil) ile 360 (hariç) derece arasında olmalı (gelen: {headingDegrees}).");
        if (timestamp == default)
            errors.Add("Zaman damgası zorunludur.");
        else if (timestamp > now + MaxClockSkew)
            errors.Add($"Zaman damgası gelecekte olamaz (gelen: {timestamp:O}, sunucu: {now:O}).");

        if (errors.Count > 0) throw new DomainValidationException(errors);

        return new PositionFix(latitude, longitude, speedMps, headingDegrees, TruncateToMicroseconds(timestamp.ToUniversalTime()));
    }

    /// <summary>
    /// PostgreSQL timestamptz mikrosaniye hassasiyetindedir, .NET ise 100 ns. Yuvarlanmazsa veritabanından okunan
    /// son zaman ile aynı bildirimin tekrarı farklı görünür ve yinelenen bildirim "yeni" sanılır.
    /// </summary>
    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value) =>
        new(value.Ticks - value.Ticks % (TimeSpan.TicksPerMillisecond / 1000), value.Offset);
}
