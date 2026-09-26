using GeoCommand.Domain.Common;
using GeoCommand.Domain.Vehicles;

namespace GeoCommand.UnitTests.Domain;

public class VehicleTests
{
    [Fact]
    public void ApplyFix_updates_position_and_moving_status()
    {
        var vehicle = new Vehicle(Guid.NewGuid(), " alfa-1 ");

        vehicle.ApplyFix(TestData.Fix(39.92, 32.85, speed: 12));

        Assert.Equal("ALFA-1", vehicle.Callsign);
        Assert.Equal(VehicleStatus.Moving, vehicle.Status);
        Assert.Equal(39.92, vehicle.LastPosition!.Y);
        Assert.Equal(32.85, vehicle.LastPosition.X);
        Assert.Equal(TestData.Now, vehicle.LastUpdateUtc);
    }

    [Fact]
    public void Slow_vehicle_is_idle()
    {
        var vehicle = new Vehicle(Guid.NewGuid(), "BRAVO-2");
        vehicle.ApplyFix(TestData.Fix(39.92, 32.85, speed: 0.1));

        Assert.Equal(VehicleStatus.Idle, vehicle.Status);
    }

    [Fact]
    public void Duplicate_or_older_fix_is_rejected()
    {
        var vehicle = new Vehicle(Guid.NewGuid(), "ALFA-1");
        vehicle.ApplyFix(TestData.Fix(39.92, 32.85));

        var duplicate = TestData.Fix(39.93, 32.86);
        var older = TestData.Fix(39.93, 32.86, at: TestData.Now.AddSeconds(-1));

        Assert.False(vehicle.IsNewerThanLastFix(duplicate));
        Assert.Throws<DomainRuleException>(() => vehicle.ApplyFix(duplicate));
        Assert.Throws<DomainRuleException>(() => vehicle.ApplyFix(older));
        Assert.Equal(39.92, vehicle.LastPosition!.Y);
    }

    [Fact]
    public void Stale_vehicle_goes_offline_once_and_comes_back_online_on_next_fix()
    {
        var vehicle = TestData.VehicleAt(39.92, 32.85); // son bildirim: Now - 1 dk

        Assert.False(vehicle.MarkOfflineIfStale(TestData.Now, TimeSpan.FromMinutes(2)));
        Assert.True(vehicle.MarkOfflineIfStale(TestData.Now, TimeSpan.FromSeconds(30)));
        Assert.False(vehicle.MarkOfflineIfStale(TestData.Now.AddMinutes(1), TimeSpan.FromSeconds(30)));
        Assert.Equal(VehicleStatus.Offline, vehicle.Status);

        var cameBack = vehicle.ApplyFix(TestData.Fix(39.921, 32.851));

        Assert.True(cameBack);
        Assert.Equal(VehicleStatus.Moving, vehicle.Status);
    }

    [Fact]
    public void Vehicle_without_any_fix_never_goes_offline()
    {
        var vehicle = new Vehicle(Guid.NewGuid(), "CHARLIE-3");

        Assert.False(vehicle.MarkOfflineIfStale(TestData.Now, TimeSpan.Zero));
        Assert.Equal(VehicleStatus.Unknown, vehicle.Status);
    }

    [Theory]
    [InlineData("")]
    [InlineData("A")]
    [InlineData("BU-CAGRI-ADI-OTUZ-IKI-KARAKTERDEN-UZUN")]
    public void Invalid_callsign_is_rejected(string callsign)
    {
        Assert.Throws<DomainValidationException>(() => new Vehicle(Guid.NewGuid(), callsign));
    }
}
