using NetTopologySuite.Geometries;

namespace GeoCommand.Domain.Zones;

public enum ZoneTransitionKind
{
    Entered,
    Exited
}

public sealed record ZoneTransition(Zone Zone, ZoneTransitionKind Kind);

/// <summary>
/// Aracın yeni konumunu önceki bölge üyelikleriyle karşılaştırarak giriş/çıkış geçişlerini hesaplar.
/// Yalnızca durum değiştiğinde geçiş üretir; aynı durumdaki ardışık konumlar olay üretmez.
/// </summary>
public static class GeofenceEvaluator
{
    public static IReadOnlyList<ZoneTransition> Evaluate(
        Point position, IEnumerable<Zone> zones, IReadOnlySet<Guid> zonesCurrentlyInside)
    {
        var transitions = new List<ZoneTransition>();
        foreach (var zone in zones)
        {
            var wasInside = zonesCurrentlyInside.Contains(zone.Id);
            var isInside = zone.Covers(position);

            if (isInside && !wasInside) transitions.Add(new ZoneTransition(zone, ZoneTransitionKind.Entered));
            else if (!isInside && wasInside) transitions.Add(new ZoneTransition(zone, ZoneTransitionKind.Exited));
        }
        return transitions;
    }
}
