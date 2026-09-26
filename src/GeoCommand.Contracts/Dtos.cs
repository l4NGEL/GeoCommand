namespace GeoCommand.Contracts;

public sealed record GeoPointDto(double Latitude, double Longitude);

public sealed record VehicleDto(
    Guid Id,
    string Callsign,
    VehicleState Status,
    double? Latitude,
    double? Longitude,
    double SpeedMps,
    double HeadingDegrees,
    DateTimeOffset? LastUpdateUtc,
    Guid? ActiveMissionId);

public sealed record PositionDto(
    double Latitude,
    double Longitude,
    double SpeedMps,
    double HeadingDegrees,
    DateTimeOffset RecordedAtUtc,
    string Source);

public sealed record PositionHistoryDto(
    Guid VehicleId,
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    IReadOnlyList<PositionDto> Points,
    double DistanceMeters,
    bool Truncated);

public sealed record ZoneDto(Guid Id, string Name, IReadOnlyList<GeoPointDto> Vertices, DateTimeOffset CreatedAtUtc);

public sealed record MissionDto(
    Guid Id,
    Guid VehicleId,
    string Description,
    MissionPriority Priority,
    MissionState Status,
    DateTimeOffset AssignedAtUtc,
    DateTimeOffset UpdatedAtUtc);

public sealed record GeoEventDto(
    Guid Id,
    GeoEventType Type,
    Guid VehicleId,
    string Callsign,
    Guid? ZoneId,
    Guid? MissionId,
    DateTimeOffset OccurredAtUtc,
    string Message,
    double? Latitude,
    double? Longitude);

public sealed record SourceStatusDto(
    string SourceType,
    bool IsRunning,
    string? ActiveScenario,
    IReadOnlyList<string> AvailableScenarios,
    string? LastError);
