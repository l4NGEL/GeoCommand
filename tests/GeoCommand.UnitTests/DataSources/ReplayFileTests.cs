using System.Globalization;
using GeoCommand.Infrastructure.DataSources.Replay;
using GeoCommand.Infrastructure.Persistence;

namespace GeoCommand.UnitTests.DataSources;

public class ReplayFileTests
{
    private static IReadOnlyList<ReplayRow> Parse(string text) => ReplayFile.Parse(new StringReader(text), "test.csv");

    [Fact]
    public void Parses_rows_skipping_comments_and_blank_lines()
    {
        var rows = Parse("""
            # yorum
            offset_seconds,callsign,latitude,longitude,speed_mps,heading_deg

            0,ALFA-1,39.9208,32.8541,12.5,90
            1.5,BRAVO-2,39.93,32.858,0,359.9
            """);

        Assert.Equal(2, rows.Count);
        Assert.Equal(new ReplayRow(4, 0, "ALFA-1", 39.9208, 32.8541, 12.5, 90), rows[0]);
        Assert.Equal(1.5, rows[1].OffsetSeconds);
    }

    [Fact]
    public void Parsing_is_culture_invariant_even_under_turkish_locale()
    {
        var previous = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("tr-TR"); // tr-TR'de ondalık ayırıcı virgüldür
            var row = Parse("offset_seconds,callsign,latitude,longitude,speed_mps,heading_deg\n0,ALFA-1,39.9208,32.8541,12.5,90")[0];
            Assert.Equal(39.9208, row.Latitude);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public void Missing_header_is_rejected()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Parse("0,ALFA-1,39.9,32.8,1,0"));

        Assert.Contains("Satır 1", ex.Message);
        Assert.Contains("başlık", ex.Message);
    }

    [Fact]
    public void Errors_include_line_numbers()
    {
        var ex = Assert.Throws<InvalidDataException>(() => Parse("""
            offset_seconds,callsign,latitude,longitude,speed_mps,heading_deg
            0,ALFA-1,39,9208,32.8541,12.5,90
            1,ALFA-1,abc,32.8541,12.5,90
            5,ALFA-1,39.92,32.85,1,0
            3,ALFA-1,39.92,32.85,1,0
            """));

        Assert.Contains("Satır 2: 6 alan bekleniyordu, 7 alan var", ex.Message);
        Assert.Contains("Satır 3: 'abc' sayı değil", ex.Message);
        Assert.Contains("Satır 5: offset_seconds bir önceki satırdan", ex.Message);
        Assert.StartsWith("test.csv", ex.Message);
    }

    [Fact]
    public void Header_only_file_is_rejected()
    {
        Assert.Throws<InvalidDataException>(() => Parse("offset_seconds,callsign,latitude,longitude,speed_mps,heading_deg\n"));
    }

    [Fact]
    public void Shipped_sample_file_is_valid_and_uses_seeded_vehicles()
    {
        var path = Path.Combine(RepoPaths.DataDirectory, "replay", "ankara-kayit.csv");
        using var reader = new StreamReader(path);

        var rows = ReplayFile.Parse(reader, "ankara-kayit.csv");

        var seeded = SeedData.Vehicles.Select(v => v.Callsign).ToHashSet();
        Assert.True(rows.Count > 100);
        Assert.All(rows, r => Assert.Contains(r.Callsign, seeded));
        Assert.True(rows.Select(r => r.Callsign).Distinct().Count() >= 3);
    }
}
