using GeoCommand.Application.Abstractions;
using GeoCommand.Application.Mapping;
using GeoCommand.Contracts;
using GeoCommand.Domain.Common;
using GeoCommand.Domain.Events;
using GeoCommand.Domain.Vehicles;
using GeoCommand.Domain.Zones;
using GeoCommand.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GeoCommand.Application.Ingestion;

public enum IngestOutcome
{
    Accepted,
    Invalid,
    UnknownVehicle,
    /// <summary>Bildirim zamanı son kayıttan yeni değil (tekrar gönderim veya sırası bozuk).</summary>
    Duplicate,
    /// <summary>Eşzamanlı başka bir yazma işlemiyle çakıştı; bildirim uygulanmadı.</summary>
    Conflict
}

public sealed record IngestResult(IngestOutcome Outcome, IReadOnlyList<string> Errors, VehicleDto? Vehicle, int EventCount)
{
    public static IngestResult Rejected(IngestOutcome outcome, params string[] errors) => new(outcome, errors, null, 0);
}

/// <summary>
/// Tüm kaynaklardan (simülatör, dosya, HTTP) gelen konum bildirimleri için tek giriş noktası:
/// doğrular, geçmişe kaydeder, bölge geçişlerini değerlendirir, olayları üretir ve istemcilere yayar.
/// </summary>
public sealed class PositionIngestionService(
    IGeoCommandDbContext db,
    IOperationsClient clients,
    VehicleLocks locks,
    TimeProvider time,
    ILogger<PositionIngestionService> logger)
{
    public async Task<IngestResult> IngestAsync(PositionReport report, CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        string callsign;
        PositionFix fix;
        try
        {
            callsign = Vehicle.NormalizeCallsign(report.Callsign);
            fix = PositionFix.Create(report.Latitude, report.Longitude, report.SpeedMps, report.HeadingDegrees, report.Timestamp, now);
        }
        catch (DomainValidationException ex)
        {
            logger.LogWarning("Geçersiz konum bildirimi reddedildi. Kaynak={Source} Çağrı={Callsign} Hatalar={Errors}",
                report.Source, report.Callsign, ex.Errors);
            return new IngestResult(IngestOutcome.Invalid, ex.Errors, null, 0);
        }

        using var _ = await locks.AcquireAsync(callsign, cancellationToken);

        var vehicle = await db.Vehicles.SingleOrDefaultAsync(v => v.Callsign == callsign, cancellationToken);
        if (vehicle is null)
        {
            logger.LogWarning("Bilinmeyen araç için bildirim reddedildi. Kaynak={Source} Çağrı={Callsign}", report.Source, callsign);
            return IngestResult.Rejected(IngestOutcome.UnknownVehicle, $"'{callsign}' çağrı adlı araç tanımlı değil.");
        }

        if (!vehicle.IsNewerThanLastFix(fix))
        {
            logger.LogDebug("Yinelenen/eski bildirim yok sayıldı. Çağrı={Callsign} Zaman={Timestamp} Son={Last}",
                callsign, fix.TimestampUtc, vehicle.LastUpdateUtc);
            return IngestResult.Rejected(IngestOutcome.Duplicate,
                $"{callsign} için {fix.TimestampUtc:O} zamanlı bildirim zaten işlenmiş veya daha yeni bir bildirim var.");
        }

        var cameBackOnline = vehicle.ApplyFix(fix);
        db.PositionRecords.Add(new PositionRecord(vehicle.Id, fix, report.Source, now));

        var events = new List<GeoEvent>();
        if (cameBackOnline) events.Add(GeoEvent.ForOnline(vehicle, fix.TimestampUtc));

        var zones = await db.Zones.AsNoTracking().ToListAsync(cancellationToken);
        var memberships = await db.ZoneMemberships.Where(m => m.VehicleId == vehicle.Id).ToListAsync(cancellationToken);
        var inside = memberships.Select(m => m.ZoneId).ToHashSet();

        foreach (var transition in GeofenceEvaluator.Evaluate(vehicle.LastPosition!, zones, inside))
        {
            if (transition.Kind == ZoneTransitionKind.Entered)
                db.ZoneMemberships.Add(new ZoneMembership(vehicle.Id, transition.Zone.Id, fix.TimestampUtc));
            else
                db.ZoneMemberships.Remove(memberships.Single(m => m.ZoneId == transition.Zone.Id));

            events.Add(GeoEvent.ForZoneTransition(vehicle, transition, fix.TimestampUtc));
        }
        db.Events.AddRange(events);

        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Ör. bölge oluşturulurken aynı anda gelen bildirim aynı üyelik satırını eklemeye çalıştı.
            // Birincil anahtar yinelenen olayı engelledi; bir sonraki bildirim tutarlı durumdan devam eder.
            logger.LogWarning(ex, "Konum bildirimi eşzamanlı bir değişiklikle çakıştı. Çağrı={Callsign}", callsign);
            return IngestResult.Rejected(IngestOutcome.Conflict, "Bildirim eşzamanlı bir değişiklikle çakıştı; tekrar gönderin.");
        }

        foreach (var e in events)
            logger.LogInformation("Olay: {EventType} Çağrı={Callsign} Bölge={ZoneId} {Message}", e.Type, callsign, e.ZoneId, e.Message);

        var dto = vehicle.ToDto();
        await Broadcast.SafeAsync(logger, () => clients.VehicleUpdated(dto));
        foreach (var e in events)
            await Broadcast.SafeAsync(logger, () => clients.EventRaised(e.ToDto(callsign)));

        return new IngestResult(IngestOutcome.Accepted, [], dto, events.Count);
    }
}
