using GeoCommand.Domain.Events;
using GeoCommand.Domain.Missions;
using GeoCommand.Domain.Vehicles;
using GeoCommand.Domain.Zones;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace GeoCommand.Infrastructure.Persistence;

// Koordinatlar PostGIS geometry sütunlarında SRID 4326 (WGS84) ile saklanır ve GIST ile indekslenir.
internal static class ColumnTypes
{
    public const string Point = "geometry(Point,4326)";
    public const string Polygon = "geometry(Polygon,4326)";
}

internal sealed class VehicleConfiguration : IEntityTypeConfiguration<Vehicle>
{
    public void Configure(EntityTypeBuilder<Vehicle> b)
    {
        b.ToTable("vehicles");
        b.HasKey(v => v.Id);
        b.Property(v => v.Callsign).HasMaxLength(32).IsRequired();
        b.HasIndex(v => v.Callsign).IsUnique();
        b.Property(v => v.Status).HasConversion<string>().HasMaxLength(16);
        b.Property(v => v.LastPosition).HasColumnType(ColumnTypes.Point);
        b.HasIndex(v => v.LastPosition).HasMethod("gist");

        b.HasData(SeedData.Vehicles.Select(v => new
        {
            v.Id,
            v.Callsign,
            Status = VehicleStatus.Unknown,
            LastSpeedMps = 0.0,
            LastHeadingDegrees = 0.0
        }));
    }
}

internal sealed class PositionRecordConfiguration : IEntityTypeConfiguration<PositionRecord>
{
    public void Configure(EntityTypeBuilder<PositionRecord> b)
    {
        b.ToTable("position_records");
        b.HasKey(p => p.Id);
        b.Property(p => p.Id).UseIdentityAlwaysColumn();
        b.Property(p => p.Location).HasColumnType(ColumnTypes.Point).IsRequired();
        b.Property(p => p.Source).HasMaxLength(16).IsRequired();
        b.HasIndex(p => new { p.VehicleId, p.RecordedAtUtc });
        b.HasOne<Vehicle>().WithMany().HasForeignKey(p => p.VehicleId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class ZoneConfiguration : IEntityTypeConfiguration<Zone>
{
    public void Configure(EntityTypeBuilder<Zone> b)
    {
        b.ToTable("zones");
        b.HasKey(z => z.Id);
        b.Property(z => z.Name).HasMaxLength(80).IsRequired();
        b.Property(z => z.Area).HasColumnType(ColumnTypes.Polygon).IsRequired();
        b.HasIndex(z => z.Area).HasMethod("gist");
    }
}

internal sealed class ZoneMembershipConfiguration : IEntityTypeConfiguration<ZoneMembership>
{
    public void Configure(EntityTypeBuilder<ZoneMembership> b)
    {
        b.ToTable("zone_memberships");
        // Aynı araç aynı bölgeye ikinci kez "girmiş" olarak kaydedilemez.
        b.HasKey(m => new { m.VehicleId, m.ZoneId });
        b.HasOne<Vehicle>().WithMany().HasForeignKey(m => m.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Zone>().WithMany().HasForeignKey(m => m.ZoneId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class MissionConfiguration : IEntityTypeConfiguration<Mission>
{
    public void Configure(EntityTypeBuilder<Mission> b)
    {
        b.ToTable("missions");
        b.HasKey(m => m.Id);
        b.Ignore(m => m.IsActive);
        b.Property(m => m.Description).HasMaxLength(Mission.MaxDescriptionLength).IsRequired();
        b.Property(m => m.Priority).HasConversion<string>().HasMaxLength(16);
        b.Property(m => m.Status).HasConversion<string>().HasMaxLength(16);
        b.HasOne<Vehicle>().WithMany().HasForeignKey(m => m.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(m => new { m.VehicleId, m.AssignedAtUtc });
        // Bir aracın en fazla bir aktif görevi olabilir; domain kuralının veritabanı düzeyindeki güvencesi.
        b.HasIndex(m => m.VehicleId)
            .IsUnique()
            .HasFilter("status IN ('Assigned', 'InProgress')")
            .HasDatabaseName("ix_missions_one_active_per_vehicle");
    }
}

internal sealed class GeoEventConfiguration : IEntityTypeConfiguration<GeoEvent>
{
    public void Configure(EntityTypeBuilder<GeoEvent> b)
    {
        b.ToTable("events");
        b.HasKey(e => e.Id);
        b.Property(e => e.Type).HasConversion<string>().HasMaxLength(32);
        b.Property(e => e.Message).HasMaxLength(700).IsRequired();
        b.Property(e => e.Location).HasColumnType(ColumnTypes.Point);
        b.HasOne<Vehicle>().WithMany().HasForeignKey(e => e.VehicleId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(e => new { e.VehicleId, e.OccurredAtUtc });
        b.HasIndex(e => e.OccurredAtUtc);
        // Bölge olayları için ek güvence: aynı araç/bölge/tür/zaman için ikinci satır yazılamaz.
        // ZoneId'ye yabancı anahtar yok: bölge silinse de geçmiş olaylar korunur.
        b.HasIndex(e => new { e.VehicleId, e.ZoneId, e.Type, e.OccurredAtUtc })
            .IsUnique()
            .HasFilter("zone_id IS NOT NULL")
            .HasDatabaseName("ix_events_zone_transition_unique");
    }
}
