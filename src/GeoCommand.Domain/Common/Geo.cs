using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace GeoCommand.Domain.Common;

/// <summary>
/// WGS84 (EPSG:4326) coğrafi koordinat yardımcıları. NetTopologySuite eksen sırası X = boylam, Y = enlem'dir.
/// </summary>
public static class Geo
{
    public const int Wgs84Srid = 4326;

    public static GeometryFactory Factory { get; } =
        NtsGeometryServices.Instance.CreateGeometryFactory(Wgs84Srid);

    public static bool IsValidLatitude(double latitude) => double.IsFinite(latitude) && latitude is >= -90 and <= 90;

    public static bool IsValidLongitude(double longitude) => double.IsFinite(longitude) && longitude is >= -180 and <= 180;

    public static void AddCoordinateErrors(List<string> errors, double latitude, double longitude, string prefix = "")
    {
        if (!IsValidLatitude(latitude)) errors.Add($"{prefix}Enlem -90 ile 90 arasında olmalı (gelen: {latitude}).");
        if (!IsValidLongitude(longitude)) errors.Add($"{prefix}Boylam -180 ile 180 arasında olmalı (gelen: {longitude}).");
    }

    public static Point CreatePoint(double latitude, double longitude)
    {
        var errors = new List<string>();
        AddCoordinateErrors(errors, latitude, longitude);
        if (errors.Count > 0) throw new DomainValidationException(errors);

        return Factory.CreatePoint(new Coordinate(longitude, latitude));
    }
}
