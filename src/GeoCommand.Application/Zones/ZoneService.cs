using GeoCommand.Application.Abstractions;
using GeoCommand.Application.Mapping;
using GeoCommand.Contracts;
using GeoCommand.Domain.Common;
using GeoCommand.Domain.Zones;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GeoCommand.Application.Zones;

public sealed class ZoneService(
    IGeoCommandDbContext db,
    IOperationsClient clients,
    TimeProvider time,
    ILogger<ZoneService> logger)
{
    public async Task<IReadOnlyList<ZoneDto>> GetZonesAsync(CancellationToken ct) =>
        (await db.Zones.AsNoTracking().OrderBy(z => z.CreatedAtUtc).ToListAsync(ct)).Select(z => z.ToDto()).ToList();

    /// <summary>
    /// Bölgeyi oluşturur. O anda bölgenin içinde bulunan araçlar için üyelik sessizce başlatılır
    /// (giriş olayı üretilmez); olaylar yalnızca bundan sonraki gerçek geçişlerde oluşur.
    /// </summary>
    public async Task<ZoneDto> CreateAsync(CreateZoneRequest? request, CancellationToken ct)
    {
        if (request is null) throw new DomainValidationException(["İstek gövdesi boş olamaz."]);

        var now = time.GetUtcNow();
        var zone = Zone.Create(request.Name,
            (request.Vertices ?? []).Select(p => (p.Latitude, p.Longitude)).ToList(), now);

        db.Zones.Add(zone);

        // Uzamsal sorgu PostGIS'te çalışır (ST_Covers), ekran koordinatlarına dayanmaz.
        var area = zone.Area;
        var vehiclesInside = await db.Vehicles.AsNoTracking()
            .Where(v => v.LastPosition != null && area.Covers(v.LastPosition))
            .Select(v => v.Id)
            .ToListAsync(ct);
        foreach (var vehicleId in vehiclesInside)
            db.ZoneMemberships.Add(new ZoneMembership(vehicleId, zone.Id, now));

        await db.SaveChangesAsync(ct);
        logger.LogInformation("Bölge oluşturuldu. Bölge={ZoneId} Ad={ZoneName} Köşe={VertexCount} İçerideki araç={InsideCount}",
            zone.Id, zone.Name, zone.Area.Shell.NumPoints - 1, vehiclesInside.Count);

        var dto = zone.ToDto();
        await Broadcast.SafeAsync(logger, () => clients.ZoneCreated(dto));
        return dto;
    }

    public async Task DeleteAsync(Guid zoneId, CancellationToken ct)
    {
        var zone = await db.Zones.SingleOrDefaultAsync(z => z.Id == zoneId, ct)
                   ?? throw new NotFoundException($"{zoneId} kimlikli bölge bulunamadı.");
        db.Zones.Remove(zone); // Üyelikler veritabanında cascade ile silinir; geçmiş olaylar korunur.
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Bölge silindi. Bölge={ZoneId} Ad={ZoneName}", zone.Id, zone.Name);

        await Broadcast.SafeAsync(logger, () => clients.ZoneDeleted(zoneId));
    }

    /// <summary>Son konumu bölgenin içinde olan araçlar (PostGIS ST_Covers).</summary>
    public async Task<IReadOnlyList<VehicleDto>> GetVehiclesInZoneAsync(Guid zoneId, CancellationToken ct)
    {
        var zone = await db.Zones.AsNoTracking().SingleOrDefaultAsync(z => z.Id == zoneId, ct)
                   ?? throw new NotFoundException($"{zoneId} kimlikli bölge bulunamadı.");
        var area = zone.Area;
        var vehicles = await db.Vehicles.AsNoTracking()
            .Where(v => v.LastPosition != null && area.Covers(v.LastPosition))
            .OrderBy(v => v.Callsign)
            .ToListAsync(ct);
        return vehicles.Select(v => v.ToDto()).ToList();
    }
}
