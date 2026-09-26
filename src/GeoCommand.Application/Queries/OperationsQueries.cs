using GeoCommand.Application.Abstractions;
using GeoCommand.Application.Mapping;
using GeoCommand.Contracts;
using GeoCommand.Domain.Common;
using Microsoft.EntityFrameworkCore;

namespace GeoCommand.Application.Queries;

/// <summary>Salt okunur sorgular: araç listesi, konum geçmişi, olay listesi.</summary>
public sealed class OperationsQueries(IGeoCommandDbContext db)
{
    public const int DefaultHistoryLimit = 5_000;
    public const int MaxHistoryLimit = 20_000;
    public static readonly TimeSpan MaxHistoryRange = TimeSpan.FromDays(31);
    public const int DefaultEventLimit = 200;
    public const int MaxEventLimit = 1_000;

    public async Task<IReadOnlyList<VehicleDto>> GetVehiclesAsync(CancellationToken ct) =>
        (await db.Vehicles.AsNoTracking().OrderBy(v => v.Callsign).ToListAsync(ct)).Select(v => v.ToDto()).ToList();

    public async Task<VehicleDto> GetVehicleAsync(Guid id, CancellationToken ct)
    {
        var vehicle = await db.Vehicles.AsNoTracking().SingleOrDefaultAsync(v => v.Id == id, ct)
                      ?? throw new NotFoundException($"{id} kimlikli araç bulunamadı.");
        return vehicle.ToDto();
    }

    public async Task<PositionHistoryDto> GetPositionHistoryAsync(
        Guid vehicleId, DateTimeOffset from, DateTimeOffset to, int? limit, CancellationToken ct)
    {
        var errors = new List<string>();
        if (from >= to) errors.Add("Başlangıç zamanı bitiş zamanından önce olmalı.");
        else if (to - from > MaxHistoryRange) errors.Add($"Sorgu aralığı en fazla {MaxHistoryRange.TotalDays} gün olabilir.");
        var take = limit ?? DefaultHistoryLimit;
        if (take is < 1 or > MaxHistoryLimit) errors.Add($"Kayıt sınırı 1-{MaxHistoryLimit} arasında olmalı.");
        if (errors.Count > 0) throw new DomainValidationException(errors);

        await EnsureVehicleExistsAsync(vehicleId, ct);

        var fromUtc = from.ToUniversalTime();
        var toUtc = to.ToUniversalTime();
        var rows = await db.PositionRecords.AsNoTracking()
            .Where(p => p.VehicleId == vehicleId && p.RecordedAtUtc >= fromUtc && p.RecordedAtUtc <= toUtc)
            .OrderBy(p => p.RecordedAtUtc)
            .Take(take + 1)
            .ToListAsync(ct);

        var truncated = rows.Count > take;
        var points = rows.Take(take).Select(p => p.ToDto()).ToList();
        return new PositionHistoryDto(vehicleId, fromUtc, toUtc, points, GeoMath.PathLengthMeters(points), truncated);
    }

    public async Task<IReadOnlyList<GeoEventDto>> GetEventsAsync(
        Guid? vehicleId, DateTimeOffset? from, DateTimeOffset? to, int? limit, CancellationToken ct)
    {
        var errors = new List<string>();
        if (from is not null && to is not null && from >= to) errors.Add("Başlangıç zamanı bitiş zamanından önce olmalı.");
        var take = limit ?? DefaultEventLimit;
        if (take is < 1 or > MaxEventLimit) errors.Add($"Kayıt sınırı 1-{MaxEventLimit} arasında olmalı.");
        if (errors.Count > 0) throw new DomainValidationException(errors);

        if (vehicleId is not null) await EnsureVehicleExistsAsync(vehicleId.Value, ct);

        var query = db.Events.AsNoTracking().AsQueryable();
        if (vehicleId is not null) query = query.Where(e => e.VehicleId == vehicleId);
        if (from is not null) { var f = from.Value.ToUniversalTime(); query = query.Where(e => e.OccurredAtUtc >= f); }
        if (to is not null) { var t = to.Value.ToUniversalTime(); query = query.Where(e => e.OccurredAtUtc <= t); }

        var events = await query.OrderByDescending(e => e.OccurredAtUtc).Take(take).ToListAsync(ct);
        var callsigns = await db.Vehicles.AsNoTracking().ToDictionaryAsync(v => v.Id, v => v.Callsign, ct);
        return events.Select(e => e.ToDto(callsigns.GetValueOrDefault(e.VehicleId, "?"))).ToList();
    }

    private async Task EnsureVehicleExistsAsync(Guid vehicleId, CancellationToken ct)
    {
        if (!await db.Vehicles.AnyAsync(v => v.Id == vehicleId, ct))
            throw new NotFoundException($"{vehicleId} kimlikli araç bulunamadı.");
    }
}
