using System.Text;
using GeoCommand.Plugins.Nmea;

namespace GeoCommand.UnitTests.Plugins;

public class NmeaLineReaderTests
{
    [Fact]
    public async Task Splits_on_crlf_and_lf_even_when_bytes_arrive_one_at_a_time()
    {
        var reader = new NmeaLineReader(new TrickleStream("$A*00\r\n$B*00\n\n$C*00\r\n"u8.ToArray()));

        Assert.Equal("$A*00", await reader.ReadLineAsync(default));
        Assert.Equal("$B*00", await reader.ReadLineAsync(default));
        Assert.Equal("", await reader.ReadLineAsync(default));
        Assert.Equal("$C*00", await reader.ReadLineAsync(default));
        Assert.Null(await reader.ReadLineAsync(default));
    }

    [Fact]
    public async Task Overlong_line_is_dropped_without_buffering_it_and_reading_resumes_at_the_next_line()
    {
        var input = Encoding.ASCII.GetBytes(new string('X', 100_000) + "\r\n$OK*00\r\n");
        var reader = new NmeaLineReader(new MemoryStream(input), maxLineLength: 16);

        Assert.Equal("$OK*00", await reader.ReadLineAsync(default));
        Assert.Equal(1, reader.DiscardedLines);
    }

    [Fact]
    public async Task Trailing_text_without_newline_is_not_returned_as_a_line()
    {
        // Bağlantı cümle ortasında koparsa yarım cümle işlenmez.
        var reader = new NmeaLineReader(new MemoryStream("$A*00\r\n$GPRMC,1235"u8.ToArray()));

        Assert.Equal("$A*00", await reader.ReadLineAsync(default));
        Assert.Null(await reader.ReadLineAsync(default));
    }

    /// <summary>Her okumada tek bayt döndürür; TCP'de cümlelerin parça parça gelmesini taklit eder.</summary>
    private sealed class TrickleStream(byte[] data) : MemoryStream(data)
    {
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], cancellationToken);
    }
}
