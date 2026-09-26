using GeoCommand.Domain.Common;
using GeoCommand.Domain.Zones;

namespace GeoCommand.UnitTests.Domain;

public class ZoneTests
{
    [Fact]
    public void Create_closes_ring_and_uses_wgs84_srid()
    {
        var zone = TestData.KizilaySquare();

        Assert.Equal(Geo.Wgs84Srid, zone.Area.SRID);
        Assert.True(zone.Area.Shell.IsClosed);
        Assert.Equal(5, zone.Area.Shell.NumPoints);
    }

    [Fact]
    public void Create_accepts_already_closed_ring_and_ignores_repeated_vertices()
    {
        var zone = Zone.Create("Tekrar", [(39.0, 32.0), (39.0, 32.0), (39.0, 32.1), (39.1, 32.1), (39.0, 32.0)], TestData.Now);

        Assert.Equal(4, zone.Area.Shell.NumPoints);
    }

    [Fact]
    public void Create_rejects_fewer_than_three_distinct_vertices()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Zone.Create("Az", [(39.0, 32.0), (39.1, 32.1), (39.0, 32.0)], TestData.Now));

        Assert.Contains(ex.Errors, e => e.Contains("en az 3"));
    }

    [Fact]
    public void Create_rejects_self_intersecting_bow_tie()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Zone.Create("Papyon", [(39.0, 32.0), (39.1, 32.1), (39.0, 32.1), (39.1, 32.0)], TestData.Now));

        Assert.Contains(ex.Errors, e => e.Contains("kendini kesiyor"));
    }

    [Fact]
    public void Create_reports_every_invalid_coordinate_and_empty_name()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            Zone.Create(" ", [(95, 32.0), (39.0, 200), (39.1, 32.1)], TestData.Now));

        Assert.Equal(3, ex.Errors.Count);
    }

    [Fact]
    public void Create_rejects_zone_crossing_antimeridian()
    {
        Assert.Throws<DomainValidationException>(() =>
            Zone.Create("Pasifik", [(0, 179), (0, -179), (1, -179), (1, 179)], TestData.Now));
    }
}
