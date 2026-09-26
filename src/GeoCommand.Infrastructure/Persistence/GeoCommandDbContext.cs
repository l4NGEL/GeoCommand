using GeoCommand.Application.Abstractions;
using GeoCommand.Domain.Events;
using GeoCommand.Domain.Missions;
using GeoCommand.Domain.Vehicles;
using GeoCommand.Domain.Zones;
using Microsoft.EntityFrameworkCore;

namespace GeoCommand.Infrastructure.Persistence;

public sealed class GeoCommandDbContext(DbContextOptions<GeoCommandDbContext> options)
    : DbContext(options), IGeoCommandDbContext
{
    public DbSet<Vehicle> Vehicles => Set<Vehicle>();
    public DbSet<PositionRecord> PositionRecords => Set<PositionRecord>();
    public DbSet<Zone> Zones => Set<Zone>();
    public DbSet<ZoneMembership> ZoneMemberships => Set<ZoneMembership>();
    public DbSet<Mission> Missions => Set<Mission>();
    public DbSet<GeoEvent> Events => Set<GeoEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(GeoCommandDbContext).Assembly);
    }
}
