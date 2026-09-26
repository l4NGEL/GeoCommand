using GeoCommand.Domain.Common;
using NetTopologySuite.Geometries;

namespace GeoCommand.Domain.Zones;

/// <summary>Operatörün harita üzerinde tanımladığı çokgen bölge (WGS84).</summary>
public sealed class Zone
{
    public const int MaxVertices = 200;

    public Guid Id { get; private set; }
    public string Name { get; private set; } = null!;
    public Polygon Area { get; private set; } = null!;
    public DateTimeOffset CreatedAtUtc { get; private set; }

    private Zone() { } // EF Core

    private Zone(Guid id, string name, Polygon area, DateTimeOffset createdAtUtc)
    {
        Id = id;
        Name = name;
        Area = area;
        CreatedAtUtc = createdAtUtc;
    }

    /// <param name="vertices">Köşeler (enlem, boylam) sırasıyla. Halkanın kapatılması gerekmez.</param>
    public static Zone Create(string? name, IReadOnlyList<(double Latitude, double Longitude)>? vertices, DateTimeOffset now)
    {
        var errors = new List<string>();
        name = name?.Trim() ?? "";
        if (name.Length is < 1 or > 80) errors.Add("Bölge adı 1-80 karakter olmalı.");

        vertices ??= [];
        for (var i = 0; i < vertices.Count; i++)
            Geo.AddCoordinateErrors(errors, vertices[i].Latitude, vertices[i].Longitude, $"{i + 1}. köşe: ");
        if (errors.Count > 0) throw new DomainValidationException(errors);

        var coordinates = new List<Coordinate>();
        foreach (var (lat, lon) in vertices)
        {
            var c = new Coordinate(lon, lat);
            if (coordinates.Count == 0 || !coordinates[^1].Equals2D(c)) coordinates.Add(c);
        }
        if (coordinates.Count > 1 && coordinates[0].Equals2D(coordinates[^1])) coordinates.RemoveAt(coordinates.Count - 1);

        if (coordinates.Count < 3) throw new DomainValidationException(["Bölge en az 3 farklı köşeden oluşmalı."]);
        if (coordinates.Count > MaxVertices) throw new DomainValidationException([$"Bölge en fazla {MaxVertices} köşe içerebilir."]);

        var lonSpan = coordinates.Max(c => c.X) - coordinates.Min(c => c.X);
        if (lonSpan >= 180)
            throw new DomainValidationException(["180. meridyeni geçen veya 180 dereceden geniş bölgeler desteklenmiyor."]);

        coordinates.Add(coordinates[0].Copy());
        var polygon = Geo.Factory.CreatePolygon(coordinates.ToArray());

        if (!polygon.IsValid) throw new DomainValidationException(["Bölge kenarları kendini kesiyor; geçerli bir çokgen çizin."]);
        if (polygon.Area <= 0) throw new DomainValidationException(["Bölgenin alanı sıfır olamaz."]);

        polygon.Normalize();
        return new Zone(Guid.NewGuid(), name, polygon, now.ToUniversalTime());
    }

    /// <summary>Nokta bölgenin içinde veya sınırı üzerinde mi. Sınır üzerindeki nokta "içeride" sayılır.</summary>
    public bool Covers(Point position) => Area.Covers(position);
}
