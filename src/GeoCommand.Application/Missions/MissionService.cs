using GeoCommand.Application.Abstractions;
using GeoCommand.Application.Mapping;
using GeoCommand.Contracts;
using GeoCommand.Domain.Common;
using GeoCommand.Domain.Events;
using GeoCommand.Domain.Missions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using DomainPriority = GeoCommand.Domain.Missions.MissionPriority;

namespace GeoCommand.Application.Missions;

/// <summary>Simülasyon içi görev atama ve durum güncelleme. Hiçbir harici sisteme komut göndermez.</summary>
public sealed class MissionService(
    IGeoCommandDbContext db,
    IOperationsClient clients,
    VehicleLocks locks,
    TimeProvider time,
    ILogger<MissionService> logger)
{
    public async Task<IReadOnlyList<MissionDto>> GetForVehicleAsync(Guid vehicleId, CancellationToken ct)
    {
        if (!await db.Vehicles.AnyAsync(v => v.Id == vehicleId, ct))
            throw new NotFoundException($"{vehicleId} kimlikli araç bulunamadı.");

        var missions = await db.Missions.AsNoTracking()
            .Where(m => m.VehicleId == vehicleId)
            .OrderByDescending(m => m.AssignedAtUtc)
            .ToListAsync(ct);
        return missions.Select(m => m.ToDto()).ToList();
    }

    public async Task<MissionDto> AssignAsync(Guid vehicleId, AssignMissionRequest? request, CancellationToken ct)
    {
        if (request is null) throw new DomainValidationException(["İstek gövdesi boş olamaz."]);

        var callsign = await db.Vehicles.Where(v => v.Id == vehicleId).Select(v => v.Callsign).SingleOrDefaultAsync(ct)
                       ?? throw new NotFoundException($"{vehicleId} kimlikli araç bulunamadı.");

        using var _ = await locks.AcquireAsync(callsign, ct);
        var vehicle = await db.Vehicles.SingleAsync(v => v.Id == vehicleId, ct);

        var mission = Mission.Assign(vehicle, request.Description, (DomainPriority)request.Priority, time.GetUtcNow());
        var geoEvent = GeoEvent.ForMissionAssigned(vehicle, mission);
        db.Missions.Add(mission);
        db.Events.Add(geoEvent);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Görev atandı. Görev={MissionId} Çağrı={Callsign} Öncelik={Priority}",
            mission.Id, vehicle.Callsign, mission.Priority);
        return await PublishAsync(mission, vehicle, geoEvent);
    }

    public async Task<MissionDto> ChangeStatusAsync(Guid missionId, ChangeMissionStatusRequest? request, CancellationToken ct)
    {
        if (request is null) throw new DomainValidationException(["İstek gövdesi boş olamaz."]);

        var info = await db.Missions.Where(m => m.Id == missionId)
                       .Join(db.Vehicles, m => m.VehicleId, v => v.Id, (m, v) => new { v.Callsign })
                       .SingleOrDefaultAsync(ct)
                   ?? throw new NotFoundException($"{missionId} kimlikli görev bulunamadı.");

        using var _ = await locks.AcquireAsync(info.Callsign, ct);
        var mission = await db.Missions.SingleAsync(m => m.Id == missionId, ct);
        var vehicle = await db.Vehicles.SingleAsync(v => v.Id == mission.VehicleId, ct);

        mission.ChangeStatus((MissionStatus)request.Status, vehicle, time.GetUtcNow());
        var geoEvent = GeoEvent.ForMissionStatusChanged(vehicle, mission);
        db.Events.Add(geoEvent);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Görev durumu değişti. Görev={MissionId} Çağrı={Callsign} Durum={Status}",
            mission.Id, vehicle.Callsign, mission.Status);
        return await PublishAsync(mission, vehicle, geoEvent);
    }

    private async Task<MissionDto> PublishAsync(Mission mission, Domain.Vehicles.Vehicle vehicle, GeoEvent geoEvent)
    {
        var dto = mission.ToDto();
        await Broadcast.SafeAsync(logger, () => clients.MissionChanged(dto));
        await Broadcast.SafeAsync(logger, () => clients.VehicleUpdated(vehicle.ToDto()));
        await Broadcast.SafeAsync(logger, () => clients.EventRaised(geoEvent.ToDto(vehicle.Callsign)));
        return dto;
    }
}
