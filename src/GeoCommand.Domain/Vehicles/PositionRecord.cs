using GeoCommand.Domain.Common;
using NetTopologySuite.Geometries;

namespace GeoCommand.Domain.Vehicles;

/// <summary>Konum geçmişindeki tek bir satır. Yalnızca eklenir, güncellenmez.</summary>
public sealed class PositionRecord
{
    public long Id { get; private set; }
    public Guid VehicleId { get; private set; }
    public Point Location { get; private set; } = null!;
    public double SpeedMps { get; private set; }
    public double HeadingDegrees { get; private set; }
    public DateTimeOffset RecordedAtUtc { get; private set; }
    public DateTimeOffset ReceivedAtUtc { get; private set; }
    /// <summary>Bildirimi üreten veri kaynağı (simulator, file, http).</summary>
    public string Source { get; private set; } = null!;

    private PositionRecord() { } // EF Core

    public PositionRecord(Guid vehicleId, PositionFix fix, string source, DateTimeOffset receivedAtUtc)
    {
        VehicleId = vehicleId;
        Location = Geo.CreatePoint(fix.Latitude, fix.Longitude);
        SpeedMps = fix.SpeedMps;
        HeadingDegrees = fix.HeadingDegrees;
        RecordedAtUtc = fix.TimestampUtc;
        ReceivedAtUtc = receivedAtUtc.ToUniversalTime();
        Source = source;
    }
}
