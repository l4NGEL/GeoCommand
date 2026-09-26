using GeoCommand.Contracts;

namespace GeoCommand.Application.Queries;

public static class GeoMath
{
    private const double EarthRadiusMeters = 6_371_008.8;

    /// <summary>İki WGS84 noktası arasındaki büyük daire mesafesi (haversine), metre.</summary>
    public static double HaversineMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var dLat = ToRad(lat2 - lat1);
        var dLon = ToRad(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2)
                + Math.Cos(ToRad(lat1)) * Math.Cos(ToRad(lat2)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * EarthRadiusMeters * Math.Asin(Math.Min(1, Math.Sqrt(a)));
    }

    public static double PathLengthMeters(IReadOnlyList<PositionDto> points)
    {
        double total = 0;
        for (var i = 1; i < points.Count; i++)
            total += HaversineMeters(points[i - 1].Latitude, points[i - 1].Longitude, points[i].Latitude, points[i].Longitude);
        return total;
    }

    private static double ToRad(double degrees) => degrees * Math.PI / 180;
}
