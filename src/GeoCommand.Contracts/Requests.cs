namespace GeoCommand.Contracts;

/// <summary>Harici bir kaynağın HTTP üzerinden gönderdiği konum bildirimi.</summary>
public sealed record PositionReportRequest(
    string Callsign,
    double Latitude,
    double Longitude,
    double SpeedMps,
    double HeadingDegrees,
    DateTimeOffset Timestamp);

public sealed record CreateZoneRequest(string Name, IReadOnlyList<GeoPointDto> Vertices);

public sealed record AssignMissionRequest(string Description, MissionPriority Priority);

public sealed record ChangeMissionStatusRequest(MissionState Status);

/// <param name="SourceType">Boş: çalışan/son seçilen kaynak. Dolu: o kaynağa geçilir (çalışan kaynak önce durdurulur).</param>
public sealed record StartSourceRequest(string? Scenario, string? SourceType = null);
