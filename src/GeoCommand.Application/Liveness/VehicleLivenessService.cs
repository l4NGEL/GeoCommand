using GeoCommand.Application.Abstractions;
using GeoCommand.Application.Mapping;
using GeoCommand.Contracts;
using GeoCommand.Domain.Events;
using GeoCommand.Domain.Vehicles;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoCommand.Application.Liveness;

public sealed class LivenessOptions
{
    public const string Section = "Liveness";

    /// <summary>Bu süre boyunca bildirim gelmeyen araç çevrimdışı sayılır.</summary>
    public int OfflineAfterSeconds { get; set; } = 15;

    public int CheckIntervalSeconds { get; set; } = 5;
}

/// <summary>Uzun süredir bildirim göndermeyen araçları çevrimdışı işaretler ve olay üretir.</summary>
public sealed class VehicleLivenessService(
    IGeoCommandDbContext db,
    IOperationsClient clients,
    VehicleLocks locks,
    TimeProvider time,
    IOptions<LivenessOptions> options,
    ILogger<VehicleLivenessService> logger)
{
    public async Task<int> MarkStaleVehiclesAsync(CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var threshold = TimeSpan.FromSeconds(options.Value.OfflineAfterSeconds);
        var cutoff = now - threshold;

        var candidates = await db.Vehicles.AsNoTracking()
            .Where(v => v.Status != VehicleStatus.Offline && v.Status != VehicleStatus.Unknown && v.LastUpdateUtc < cutoff)
            .Select(v => new { v.Id, v.Callsign })
            .ToListAsync(ct);

        var marked = 0;
        foreach (var candidate in candidates)
        {
            using var _ = await locks.AcquireAsync(candidate.Callsign, ct);
            var vehicle = await db.Vehicles.SingleAsync(v => v.Id == candidate.Id, ct);
            if (!vehicle.MarkOfflineIfStale(now, threshold)) continue; // bu arada yeni bildirim gelmiş

            var geoEvent = GeoEvent.ForOffline(vehicle, now);
            db.Events.Add(geoEvent);
            await db.SaveChangesAsync(ct);
            marked++;

            logger.LogWarning("Araç çevrimdışı. Çağrı={Callsign} SonBildirim={LastUpdate}", vehicle.Callsign, vehicle.LastUpdateUtc);
            await Broadcast.SafeAsync(logger, () => clients.VehicleUpdated(vehicle.ToDto()));
            await Broadcast.SafeAsync(logger, () => clients.EventRaised(geoEvent.ToDto(vehicle.Callsign)));
        }
        return marked;
    }
}
