using GeoCommand.Domain.Common;
using GeoCommand.Domain.Vehicles;

namespace GeoCommand.UnitTests.Domain;

public class PositionFixTests
{
    [Fact]
    public void Valid_fix_is_normalized_to_utc()
    {
        var local = new DateTimeOffset(2026, 9, 26, 13, 0, 0, TimeSpan.FromHours(3));

        var fix = PositionFix.Create(39.92, 32.85, 10, 45, local, TestData.Now);

        Assert.Equal(TimeSpan.Zero, fix.TimestampUtc.Offset);
        Assert.Equal(TestData.Now, fix.TimestampUtc);
    }

    [Theory]
    [InlineData(91, 32, 10, 0)]
    [InlineData(39, -181, 10, 0)]
    [InlineData(39, 32, -1, 0)]
    [InlineData(39, 32, 1000, 0)]
    [InlineData(39, 32, 10, 360)]
    [InlineData(double.NaN, 32, 10, 0)]
    public void Out_of_range_values_are_rejected(double lat, double lon, double speed, double heading)
    {
        Assert.Throws<DomainValidationException>(() =>
            PositionFix.Create(lat, lon, speed, heading, TestData.Now, TestData.Now));
    }

    [Fact]
    public void Timestamp_in_future_beyond_clock_skew_is_rejected()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            PositionFix.Create(39, 32, 1, 0, TestData.Now.AddMinutes(1), TestData.Now));

        Assert.Contains(ex.Errors, e => e.Contains("gelecekte"));
    }

    [Fact]
    public void Small_clock_skew_is_tolerated()
    {
        var fix = PositionFix.Create(39, 32, 1, 0, TestData.Now.AddSeconds(3), TestData.Now);

        Assert.Equal(TestData.Now.AddSeconds(3), fix.TimestampUtc);
    }

    [Fact]
    public void Timestamp_is_truncated_to_database_precision_so_resends_are_detected_as_duplicates()
    {
        var precise = TestData.Now.AddTicks(1_234_567); // 0,1234567 sn
        var vehicle = new Vehicle(Guid.NewGuid(), "ALFA-1");
        vehicle.ApplyFix(PositionFix.Create(39, 32, 1, 0, precise, TestData.Now.AddSeconds(1)));

        var resend = PositionFix.Create(39, 32, 1, 0, precise, TestData.Now.AddSeconds(1));

        Assert.Equal(TestData.Now.AddTicks(1_234_560), resend.TimestampUtc);
        Assert.False(vehicle.IsNewerThanLastFix(resend));
    }

    [Fact]
    public void All_errors_are_reported_together()
    {
        var ex = Assert.Throws<DomainValidationException>(() =>
            PositionFix.Create(100, 200, -5, 400, default, TestData.Now));

        Assert.Equal(5, ex.Errors.Count);
    }
}
