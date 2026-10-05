using GeoCommand.Contracts;
using Google.Protobuf.WellKnownTypes;
using Proto = GeoCommand.Grpc.V1;

namespace GeoCommand.Api.GrpcServices;

/// <summary>Contracts DTO'ları ile Protobuf mesajları arasında dönüşüm. Kimlikler metin GUID olarak taşınır.</summary>
internal static class GrpcMapping
{
    public const string ReportSource = "grpc";

    /// <summary>Zaman damgası yoksa null: alma hattı <see cref="DateTimeOffset.MinValue"/>'u da reddederdi, ama hata mesajı belirsiz olurdu.</summary>
    public static Sdk.PositionReport? ToReport(this Proto.PositionReport message) =>
        message.Timestamp is null
            ? null
            : new Sdk.PositionReport(message.Callsign, message.Latitude, message.Longitude, message.SpeedMps,
                message.HeadingDegrees, message.Timestamp.ToDateTimeOffset(), ReportSource);

    public static Proto.Vehicle ToMessage(this VehicleDto v)
    {
        var message = new Proto.Vehicle
        {
            Id = v.Id.ToString(),
            Callsign = v.Callsign,
            State = v.Status switch
            {
                VehicleState.Idle => Proto.VehicleState.Idle,
                VehicleState.Moving => Proto.VehicleState.Moving,
                VehicleState.OnMission => Proto.VehicleState.OnMission,
                VehicleState.Offline => Proto.VehicleState.Offline,
                _ => Proto.VehicleState.Unknown
            },
            SpeedMps = v.SpeedMps,
            HeadingDegrees = v.HeadingDegrees,
            LastUpdate = v.LastUpdateUtc is { } at ? Timestamp.FromDateTimeOffset(at) : null,
            ActiveMissionId = v.ActiveMissionId?.ToString() ?? ""
        };
        if (v.Latitude is { } lat) message.Latitude = lat;
        if (v.Longitude is { } lon) message.Longitude = lon;
        return message;
    }

    public static Proto.GeoEvent ToMessage(this GeoEventDto e)
    {
        var message = new Proto.GeoEvent
        {
            Id = e.Id.ToString(),
            Type = e.Type switch
            {
                GeoEventType.ZoneEntered => Proto.GeoEventType.ZoneEntered,
                GeoEventType.ZoneExited => Proto.GeoEventType.ZoneExited,
                GeoEventType.MissionAssigned => Proto.GeoEventType.MissionAssigned,
                GeoEventType.MissionStatusChanged => Proto.GeoEventType.MissionStatusChanged,
                GeoEventType.VehicleOffline => Proto.GeoEventType.VehicleOffline,
                GeoEventType.VehicleOnline => Proto.GeoEventType.VehicleOnline,
                _ => Proto.GeoEventType.Unspecified
            },
            VehicleId = e.VehicleId.ToString(),
            Callsign = e.Callsign,
            ZoneId = e.ZoneId?.ToString() ?? "",
            MissionId = e.MissionId?.ToString() ?? "",
            OccurredAt = Timestamp.FromDateTimeOffset(e.OccurredAtUtc),
            Message = e.Message
        };
        if (e.Latitude is { } lat) message.Latitude = lat;
        if (e.Longitude is { } lon) message.Longitude = lon;
        return message;
    }
}
