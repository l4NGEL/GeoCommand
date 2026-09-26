namespace GeoCommand.Domain.Zones;

/// <summary>
/// Bir aracın şu anda bir bölgenin içinde olduğunu kaydeder. Satır yalnızca araç içerideyken vardır;
/// (araç, bölge) birincil anahtarı aynı giriş olayının iki kez işlenmesini veritabanı düzeyinde de engeller.
/// </summary>
public sealed class ZoneMembership
{
    public Guid VehicleId { get; private set; }
    public Guid ZoneId { get; private set; }
    public DateTimeOffset EnteredAtUtc { get; private set; }

    private ZoneMembership() { } // EF Core

    public ZoneMembership(Guid vehicleId, Guid zoneId, DateTimeOffset enteredAtUtc)
    {
        VehicleId = vehicleId;
        ZoneId = zoneId;
        EnteredAtUtc = enteredAtUtc.ToUniversalTime();
    }
}
