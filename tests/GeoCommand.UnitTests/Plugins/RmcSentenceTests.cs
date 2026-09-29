using GeoCommand.Plugins.Nmea;

namespace GeoCommand.UnitTests.Plugins;

public class RmcSentenceTests
{
    // Beklenen değerler tools/nmea_emitter.py ile bağımsız olarak üretildi.
    private const string Ankara = "$GPRMC,123519.50,A,3955.2480,N,03251.2460,E,24.3,152.9,280926,,,A*6A";

    [Fact]
    public void Parses_position_speed_course_and_utc_time()
    {
        var status = RmcSentence.TryParse(Ankara, out var fix, out var error);

        Assert.Equal(RmcParseStatus.Ok, status);
        Assert.Null(error);
        Assert.Equal(39.9208, fix!.Latitude, 6);
        Assert.Equal(32.8541, fix.Longitude, 6);
        Assert.Equal(24.3 * 1852 / 3600, fix.SpeedMps, 6);
        Assert.Equal(152.9, fix.CourseDegrees);
        Assert.Equal(new DateTimeOffset(2026, 9, 28, 12, 35, 19, 500, TimeSpan.Zero), fix.TimestampUtc);
    }

    [Fact]
    public void Southern_and_western_hemispheres_are_negative()
    {
        var status = RmcSentence.TryParse("$GPRMC,000000.00,A,3352.1280,S,15112.5580,W,0.0,0.0,280926,,,A*54", out var fix, out _);

        Assert.Equal(RmcParseStatus.Ok, status);
        Assert.Equal(-33.8688, fix!.Latitude, 6);
        Assert.Equal(-151.2093, fix.Longitude, 6);
    }

    [Fact]
    public void Other_talkers_whole_seconds_and_empty_course_are_accepted()
    {
        var status = RmcSentence.TryParse("$GNRMC,235959,A,3955.2480,N,03251.2460,E,0.0,,311226,,*28", out var fix, out _);

        Assert.Equal(RmcParseStatus.Ok, status);
        Assert.Null(fix!.CourseDegrees);
        Assert.Equal(new DateTimeOffset(2026, 12, 31, 23, 59, 59, TimeSpan.Zero), fix.TimestampUtc);
    }

    [Fact]
    public void Receiver_without_fix_is_reported_separately_from_errors()
    {
        Assert.Equal(RmcParseStatus.NoFix, RmcSentence.TryParse("$GPRMC,123519.00,V,,,,,,,280926,,,N*77", out var fix, out _));
        Assert.Null(fix);
    }

    [Fact]
    public void Other_sentence_types_are_skipped()
    {
        var gga = "$GPGGA,123519.00,3955.2480,N,03251.2460,E,1,08,0.9,545.4,M,46.9,M,,*6A";
        Assert.Equal(RmcParseStatus.NotRmc, RmcSentence.TryParse(gga, out _, out _));
    }

    [Theory]
    [InlineData("$GPRMC,123519.50,A,3955.2480,N,03251.2460,E,24.3,152.9,280926,,,A*6B", "sağlama toplamı hatalı")]
    [InlineData("$GPRMC,123519.50,A,3955.2480,N,03251.2460,E,24.3,152.9,280926,,,A", "sağlama toplamı yok")]
    [InlineData("GPRMC,123519.50,A,3955.2480,N,03251.2460,E,24.3,152.9,280926,,,A*6A", "'$'")]
    [InlineData("$GPRMC,123519.50,A*ZZ", "onaltılık")]
    public void Corrupted_sentences_are_rejected_with_a_reason(string line, string expectedError)
    {
        Assert.Equal(RmcParseStatus.Invalid, RmcSentence.TryParse(line, out var fix, out var error));
        Assert.Null(fix);
        Assert.Contains(expectedError, error);
    }

    [Theory]
    [InlineData("GPRMC,123519.50,A,9055.2480,N,03251.2460,E,24.3,152.9,280926,,,A", "enlem")]      // 90°55' > 90
    [InlineData("GPRMC,123519.50,A,3975.0000,N,03251.2460,E,24.3,152.9,280926,,,A", "enlem")]      // dakika >= 60
    [InlineData("GPRMC,123519.50,A,3955.2480,X,03251.2460,E,24.3,152.9,280926,,,A", "enlem")]      // yarıküre
    [InlineData("GPRMC,123519.50,A,3955.2480,N,18251.2460,E,24.3,152.9,280926,,,A", "boylam")]
    [InlineData("GPRMC,123519.50,A,3955.2480,N,03251.2460,E,-1,152.9,280926,,,A", "hız")]
    [InlineData("GPRMC,123519.50,A,3955.2480,N,03251.2460,E,24.3,361,280926,,,A", "rota")]
    [InlineData("GPRMC,123519.50,A,3955.2480,N,03251.2460,E,24.3,152.9,300226,,,A", "tarih")]      // 30 Şubat
    [InlineData("GPRMC,246000.00,A,3955.2480,N,03251.2460,E,24.3,152.9,280926,,,A", "saat")]
    [InlineData("GPRMC,123519.50,X,3955.2480,N,03251.2460,E,24.3,152.9,280926,,,A", "durum")]
    [InlineData("GPRMC,123519.50,A,3955.2480,N", "10 alan")]
    public void Invalid_field_values_are_rejected_even_with_a_correct_checksum(string body, string expectedError)
    {
        var line = $"${body}*{RmcSentence.Checksum(body):X2}";

        Assert.Equal(RmcParseStatus.Invalid, RmcSentence.TryParse(line, out _, out var error));
        Assert.Contains(expectedError, error);
    }

    [Fact]
    public void Overlong_input_is_rejected_before_parsing()
    {
        Assert.Equal(RmcParseStatus.Invalid, RmcSentence.TryParse("$" + new string('A', RmcSentence.MaxLength) + "*00", out _, out var error));
        Assert.Contains("uzun", error);
    }

    [Fact]
    public void Format_produces_the_same_sentence_as_the_independent_python_emitter()
    {
        var fix = new RmcFix(new DateTimeOffset(2026, 9, 28, 12, 35, 19, 500, TimeSpan.Zero), 39.9208, 32.8541, 12.5, 152.9);

        Assert.Equal(Ankara, RmcSentence.Format(fix));
    }

    [Theory]
    [InlineData(39.9208, 32.8541, 12.5, 152.9)]
    [InlineData(-33.8688, -151.2093, 0.0, null)]
    [InlineData(0.00001, 179.99999, 280.0, 359.9)]
    public void Format_and_parse_round_trip(double lat, double lon, double speed, double? course)
    {
        var original = new RmcFix(new DateTimeOffset(2026, 1, 2, 3, 4, 5, 60, TimeSpan.Zero), lat, lon, speed, course);

        Assert.Equal(RmcParseStatus.Ok, RmcSentence.TryParse(RmcSentence.Format(original), out var parsed, out _));
        Assert.Equal(original.TimestampUtc, parsed!.TimestampUtc);
        Assert.Equal(lat, parsed.Latitude, 5);   // 0.0001 dakika ≈ 0,2 m
        Assert.Equal(lon, parsed.Longitude, 5);
        Assert.Equal(speed, parsed.SpeedMps, 1); // 0.1 knot çözünürlük
        Assert.Equal(course, parsed.CourseDegrees);
    }
}
