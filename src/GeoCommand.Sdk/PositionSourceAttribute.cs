using System.Composition;

namespace GeoCommand.Sdk;

/// <summary>
/// Bir sınıfı MEF (<c>System.Composition</c>) üzerinden <see cref="IPositionSource"/> olarak dışa aktarır ve
/// kaynağı örneklemeden okunabilen meta veriyi (<see cref="PositionSourceMetadata"/>) ekler.
/// </summary>
/// <example>
/// <code>
/// [PositionSource("Nmea", Description = "NMEA 0183 ($GPRMC) TCP akışı")]
/// public sealed class NmeaPositionSource : IPositionSource { ... }
/// </code>
/// </example>
[MetadataAttribute]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
public sealed class PositionSourceAttribute(string name) : ExportAttribute(typeof(IPositionSource))
{
    /// <summary>Yapılandırmada ve API'de kullanılan tekil ad.</summary>
    public string Name { get; } = name;

    /// <summary>Operatöre gösterilen kısa açıklama.</summary>
    public string Description { get; set; } = "";
}

/// <summary>MEF meta veri görünümü; <see cref="PositionSourceAttribute"/> özelliklerinden doldurulur.</summary>
public sealed class PositionSourceMetadata
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
}
