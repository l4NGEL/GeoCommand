using GeoCommand.Domain.Common;
using GeoCommand.Domain.Zones;

namespace GeoCommand.UnitTests.Domain;

public class GeofenceEvaluatorTests
{
    private static readonly Zone Zone = TestData.KizilaySquare();

    [Fact]
    public void Outside_to_inside_produces_single_entered_transition()
    {
        var inside = Geo.CreatePoint(39.920, 32.856);

        var transitions = GeofenceEvaluator.Evaluate(inside, [Zone], new HashSet<Guid>());

        var t = Assert.Single(transitions);
        Assert.Equal(ZoneTransitionKind.Entered, t.Kind);
        Assert.Equal(Zone.Id, t.Zone.Id);
    }

    [Fact]
    public void Inside_to_outside_produces_single_exited_transition()
    {
        var outside = Geo.CreatePoint(39.930, 32.856);

        var transitions = GeofenceEvaluator.Evaluate(outside, [Zone], new HashSet<Guid> { Zone.Id });

        Assert.Equal(ZoneTransitionKind.Exited, Assert.Single(transitions).Kind);
    }

    [Fact]
    public void Staying_inside_produces_no_transition()
    {
        var inside = Geo.CreatePoint(39.921, 32.857);

        Assert.Empty(GeofenceEvaluator.Evaluate(inside, [Zone], new HashSet<Guid> { Zone.Id }));
    }

    [Fact]
    public void Staying_outside_produces_no_transition()
    {
        var outside = Geo.CreatePoint(39.900, 32.800);

        Assert.Empty(GeofenceEvaluator.Evaluate(outside, [Zone], new HashSet<Guid>()));
    }

    [Fact]
    public void Point_on_boundary_counts_as_inside()
    {
        var onEdge = Geo.CreatePoint(39.915, 32.856);

        Assert.Equal(ZoneTransitionKind.Entered,
            Assert.Single(GeofenceEvaluator.Evaluate(onEdge, [Zone], new HashSet<Guid>())).Kind);
    }

    [Fact]
    public void Point_about_50_metres_outside_edge_is_outside()
    {
        // 0.00045 derece enlem ≈ 50 m. Ekran pikseline değil, WGS84 koordinatlarına göre değerlendirilir.
        var justNorth = Geo.CreatePoint(39.925 + 0.00045, 32.856);

        Assert.Empty(GeofenceEvaluator.Evaluate(justNorth, [Zone], new HashSet<Guid>()));
    }

    [Fact]
    public void Moving_between_overlapping_zones_reports_each_zone_independently()
    {
        var east = Zone.Create("Doğu", [(39.915, 32.860), (39.915, 32.870), (39.925, 32.870), (39.925, 32.860)], TestData.Now);
        // Sadece doğu bölgesinde; batı (Kızılay) bölgesinden çıktı.
        var position = Geo.CreatePoint(39.920, 32.866);

        var transitions = GeofenceEvaluator.Evaluate(position, [Zone, east], new HashSet<Guid> { Zone.Id });

        Assert.Equal(2, transitions.Count);
        Assert.Contains(transitions, t => t.Zone.Id == Zone.Id && t.Kind == ZoneTransitionKind.Exited);
        Assert.Contains(transitions, t => t.Zone.Id == east.Id && t.Kind == ZoneTransitionKind.Entered);
    }

    [Fact]
    public void Concave_zone_uses_true_polygon_not_bounding_box()
    {
        // "L" biçimli bölge; sağ üst köşe sınır kutusunun içinde ama çokgenin dışında.
        var lShape = Zone.Create("L", [(39.90, 32.80), (39.90, 32.84), (39.92, 32.84), (39.92, 32.82), (39.94, 32.82), (39.94, 32.80)], TestData.Now);
        var notch = Geo.CreatePoint(39.93, 32.83);

        Assert.Empty(GeofenceEvaluator.Evaluate(notch, [lShape], new HashSet<Guid>()));
    }
}
