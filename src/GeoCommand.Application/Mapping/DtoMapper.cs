using GeoCommand.Contracts;
using GeoCommand.Domain.Events;
using GeoCommand.Domain.Missions;
using GeoCommand.Domain.Vehicles;
using GeoCommand.Domain.Zones;
using ContractEventType = GeoCommand.Contracts.GeoEventType;
using ContractPriority = GeoCommand.Contracts.MissionPriority;

namespace GeoCommand.Application.Mapping;

public static class DtoMapper
{
    public static VehicleDto ToDto(this Vehicle v) => new(
        v.Id, v.Callsign, (VehicleState)v.Status,
        v.LastPosition?.Y, v.LastPosition?.X,
        v.LastSpeedMps, v.LastHeadingDegrees, v.LastUpdateUtc, v.ActiveMissionId);

    public static PositionDto ToDto(this PositionRecord p) => new(
        p.Location.Y, p.Location.X, p.SpeedMps, p.HeadingDegrees, p.RecordedAtUtc, p.Source);

    public static ZoneDto ToDto(this Zone z)
    {
        var shell = z.Area.Shell.Coordinates;
        // Kapalı halkanın son noktası ilk noktanın tekrarıdır; istemciye göndermiyoruz.
        var vertices = shell.Take(shell.Length - 1).Select(c => new GeoPointDto(c.Y, c.X)).ToList();
        return new ZoneDto(z.Id, z.Name, vertices, z.CreatedAtUtc);
    }

    public static MissionDto ToDto(this Mission m) => new(
        m.Id, m.VehicleId, m.Description, (ContractPriority)m.Priority, (MissionState)m.Status,
        m.AssignedAtUtc, m.UpdatedAtUtc);

    public static GeoEventDto ToDto(this GeoEvent e, string callsign) => new(
        e.Id, (ContractEventType)e.Type, e.VehicleId, callsign, e.ZoneId, e.MissionId,
        e.OccurredAtUtc, e.Message, e.Location?.Y, e.Location?.X);
}
