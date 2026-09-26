using GeoCommand.Domain.Events;
using GeoCommand.Domain.Missions;
using GeoCommand.Domain.Vehicles;
using GeoCommand.Domain.Zones;
using Microsoft.EntityFrameworkCore;

namespace GeoCommand.Application.Abstractions;

/// <summary>
/// Application katmanının veritabanına eriştiği tek nokta. Uygulaması Infrastructure'daki EF Core
/// (PostgreSQL/PostGIS) DbContext'idir; Application yalnızca EF Core soyutlamalarına bağımlıdır.
/// </summary>
public interface IGeoCommandDbContext
{
    DbSet<Vehicle> Vehicles { get; }
    DbSet<PositionRecord> PositionRecords { get; }
    DbSet<Zone> Zones { get; }
    DbSet<ZoneMembership> ZoneMemberships { get; }
    DbSet<Mission> Missions { get; }
    DbSet<GeoEvent> Events { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
