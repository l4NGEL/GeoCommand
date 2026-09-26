using GeoCommand.Domain.Vehicles;
using GeoCommand.Domain.Zones;

namespace GeoCommand.UnitTests;

internal static class TestData
{
    public static readonly DateTimeOffset Now = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);

    /// <summary>Ankara Kızılay çevresinde yaklaşık 1,1 km x 1,1 km'lik kare.</summary>
    public static Zone KizilaySquare(string name = "Kızılay") => Zone.Create(name,
    [
        (39.915, 32.850),
        (39.915, 32.863),
        (39.925, 32.863),
        (39.925, 32.850)
    ], Now);

    public static PositionFix Fix(double lat, double lon, double speed = 10, DateTimeOffset? at = null) =>
        PositionFix.Create(lat, lon, speed, 90, at ?? Now, Now);

    public static Vehicle VehicleAt(double lat, double lon, string callsign = "ALFA-1")
    {
        var vehicle = new Vehicle(Guid.NewGuid(), callsign);
        vehicle.ApplyFix(Fix(lat, lon, at: Now.AddMinutes(-1)));
        return vehicle;
    }
}
