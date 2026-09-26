using GeoCommand.Domain.Missions;
using GeoCommand.Domain.Vehicles;
using GeoCommand.Domain.Zones;
using NetTopologySuite.Geometries;

namespace GeoCommand.Domain.Events;

public enum GeoEventType
{
    ZoneEntered = 0,
    ZoneExited = 1,
    MissionAssigned = 2,
    MissionStatusChanged = 3,
    VehicleOffline = 4,
    VehicleOnline = 5
}

/// <summary>Operatörün olay listesinde gördüğü, kalıcı olarak saklanan olay.</summary>
public sealed class GeoEvent
{
    public Guid Id { get; private set; }
    public GeoEventType Type { get; private set; }
    public Guid VehicleId { get; private set; }
    public Guid? ZoneId { get; private set; }
    public Guid? MissionId { get; private set; }
    public DateTimeOffset OccurredAtUtc { get; private set; }
    public string Message { get; private set; } = null!;
    public Point? Location { get; private set; }

    private GeoEvent() { } // EF Core

    private GeoEvent(GeoEventType type, Vehicle vehicle, DateTimeOffset occurredAt, string message,
        Guid? zoneId = null, Guid? missionId = null)
    {
        Id = Guid.NewGuid();
        Type = type;
        VehicleId = vehicle.Id;
        ZoneId = zoneId;
        MissionId = missionId;
        OccurredAtUtc = occurredAt.ToUniversalTime();
        Message = message;
        Location = vehicle.LastPosition?.Copy() as Point;
    }

    public static GeoEvent ForZoneTransition(Vehicle vehicle, ZoneTransition transition, DateTimeOffset occurredAt) =>
        transition.Kind == ZoneTransitionKind.Entered
            ? new(GeoEventType.ZoneEntered, vehicle, occurredAt, $"{vehicle.Callsign} '{transition.Zone.Name}' bölgesine girdi.", zoneId: transition.Zone.Id)
            : new(GeoEventType.ZoneExited, vehicle, occurredAt, $"{vehicle.Callsign} '{transition.Zone.Name}' bölgesinden çıktı.", zoneId: transition.Zone.Id);

    public static GeoEvent ForMissionAssigned(Vehicle vehicle, Mission mission) =>
        new(GeoEventType.MissionAssigned, vehicle, mission.AssignedAtUtc,
            $"{vehicle.Callsign} aracına {mission.Priority.ToTurkish()} öncelikli görev atandı: {mission.Description}", missionId: mission.Id);

    public static GeoEvent ForMissionStatusChanged(Vehicle vehicle, Mission mission) =>
        new(GeoEventType.MissionStatusChanged, vehicle, mission.UpdatedAtUtc,
            $"{vehicle.Callsign} görevi '{mission.Status.ToTurkish()}' durumuna geçti.", missionId: mission.Id);

    public static GeoEvent ForOffline(Vehicle vehicle, DateTimeOffset occurredAt) =>
        new(GeoEventType.VehicleOffline, vehicle, occurredAt, $"{vehicle.Callsign} bildirim göndermiyor; çevrimdışı olarak işaretlendi.");

    public static GeoEvent ForOnline(Vehicle vehicle, DateTimeOffset occurredAt) =>
        new(GeoEventType.VehicleOnline, vehicle, occurredAt, $"{vehicle.Callsign} yeniden bildirim göndermeye başladı.");
}
