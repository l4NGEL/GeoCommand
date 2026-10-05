using System.Collections.Concurrent;
using System.Text;
using GeoCommand.Infrastructure.DataSources.Plugins;
using GeoCommand.Plugins.Nmea;
using GeoCommand.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoCommand.UnitTests.Plugins;

/// <summary>
/// Kaynağın bağlantı yaşam döngüsü (yeniden bağlanma, iptal, kalıcı hata) sahte bir bağlantıyla, seri port donanımı
/// veya sanal port sürücüsü olmadan sınanır. Gerçek <see cref="System.IO.Ports.SerialPort"/> ile açılış
/// <see cref="PositionSourceCatalogTests"/>'te denenir.
/// </summary>
public class NmeaPositionSourceTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 5, 9, 0, 0, TimeSpan.Zero);

    private static NmeaPositionSource Source(FakeConnector connector, params (string Key, string Value)[] streams)
    {
        var settings = new List<(string, string)> { ("DataSource:Nmea:ReconnectSeconds", "0.1") };
        settings.AddRange(streams.Select(s => ("DataSource:Nmea:Scenarios:test:" + s.Key, s.Value)));
        var host = new PositionSourceHost(
            new ConfigurationBuilder().AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Item1, s.Item2))).Build(),
            NullLoggerFactory.Instance,
            TimeProvider.System);
        return new NmeaPositionSource(host, connector);
    }

    private static byte[] Sentences(params RmcFix[] fixes) =>
        Encoding.ASCII.GetBytes(string.Concat(fixes.Select(f => RmcSentence.Format(f) + "\r\n")));

    private static async Task<List<PositionReport>> TakeAsync(IPositionSource source, int count)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var reports = new List<PositionReport>();
        await foreach (var report in source.ReadAsync("test", timeout.Token))
        {
            reports.Add(report);
            if (reports.Count == count) break;
        }
        return reports;
    }

    [Fact]
    public async Task Serial_port_that_is_busy_or_unplugged_is_retried_until_it_opens()
    {
        var connector = new FakeConnector(
            _ => throw new IOException("COM5 başka bir uygulama tarafından kullanılıyor."),
            _ => throw new IOException("The port 'COM5' does not exist."),
            _ => new SerialLikeStream(Sentences(new RmcFix(At, 39.92, 32.85, 5, 45))));
        var source = Source(connector, ("0:Callsign", "ALFA-1"), ("0:SerialPort", "COM5"));

        var report = Assert.Single(await TakeAsync(source, 1));

        Assert.Equal(("ALFA-1", At, "nmea"), (report.Callsign, report.Timestamp, report.Source));
        Assert.Equal(3, connector.Attempts.Count);
        Assert.All(connector.Attempts, e => Assert.Equal(new SerialEndpoint("COM5", 4800, System.IO.Ports.Parity.None, 8, System.IO.Ports.StopBits.One), e));
    }

    [Fact]
    public async Task Line_dropping_mid_stream_reconnects_and_keeps_the_last_known_course()
    {
        var connector = new FakeConnector(
            _ => new SerialLikeStream(Sentences(new RmcFix(At, 39.92, 32.85, 5, 120)), endAfterData: true), // kablo çekildi
            _ => new SerialLikeStream(Sentences(new RmcFix(At.AddSeconds(1), 39.921, 32.851, 0, null))));
        var source = Source(connector, ("0:Callsign", "ALFA-1"), ("0:SerialPort", "COM5"));

        var reports = await TakeAsync(source, 2);

        Assert.Equal(2, connector.Attempts.Count);
        Assert.Equal(At.AddSeconds(1), reports[1].Timestamp);
        Assert.Equal(120, reports[1].HeadingDegrees);
    }

    [Fact]
    public async Task Stopping_aborts_a_pending_serial_read_that_ignores_the_cancellation_token()
    {
        var line = new SerialLikeStream(Sentences(new RmcFix(At, 39.92, 32.85, 5, 45)));
        var source = Source(new FakeConnector(_ => line), ("0:Callsign", "ALFA-1"), ("0:SerialPort", "COM5"));

        using var stop = new CancellationTokenSource();
        var reading = Task.Run(async () =>
        {
            try
            {
                await foreach (var _ in source.ReadAsync("test", stop.Token)) await stop.CancelAsync();
            }
            catch (OperationCanceledException)
            {
            }
        });

        // Belirteç okumayı bitiremez; kaynak akışı kapatmazsa bu görev hiç tamamlanmaz.
        await reading.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(line.IsDisposed);
    }

    [Fact]
    public async Task Non_retryable_error_stops_the_source_and_names_the_stream()
    {
        var connector = new FakeConnector(_ => throw new ArgumentException("Geçersiz port adı."));
        var source = Source(connector,
            ("0:Callsign", "ALFA-1"), ("0:SerialPort", "LPT1"),
            ("1:Callsign", "BRAVO-2"), ("1:Host", "127.0.0.1"), ("1:Port", "10110"));
        connector.Fallback = _ => new SerialLikeStream([]); // BRAVO-2 bağlanır ama veri gelmez

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => TakeAsync(source, 1));

        Assert.Contains("ALFA-1 ← LPT1 (4800 8N1)", ex.Message);
        Assert.Contains("Geçersiz port adı.", ex.Message);
    }

    /// <summary>Sıradaki bağlantı denemesinin davranışını sırayla döndürür; sıra biterse <see cref="Fallback"/>.</summary>
    private sealed class FakeConnector(params Func<NmeaEndpoint, Stream>[] attempts) : INmeaConnector
    {
        private readonly ConcurrentQueue<Func<NmeaEndpoint, Stream>> _attempts = new(attempts);

        public ConcurrentQueue<NmeaEndpoint> Attempts { get; } = new();

        public Func<NmeaEndpoint, Stream>? Fallback { get; set; }

        public Task<Stream> ConnectAsync(NmeaEndpoint endpoint, CancellationToken cancellationToken)
        {
            if (endpoint is TcpEndpoint && Fallback is not null) return Task.FromResult(Fallback(endpoint));
            Attempts.Enqueue(endpoint);
            var next = _attempts.TryDequeue(out var a) ? a : Fallback ?? (_ => new SerialLikeStream([]));
            return Task.FromResult(next(endpoint));
        }
    }

    /// <summary>
    /// Windows seri portu gibi davranır: veriyi verdikten sonra okuma, iptal belirtecini yok sayarak yeni veri veya
    /// kapatılma bekler. <c>endAfterData</c>: veri bitince akış kapanır (bağlantı koptu).
    /// </summary>
    private sealed class SerialLikeStream(byte[] data, bool endAfterData = false) : Stream
    {
        private readonly TaskCompletionSource _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _position;

        public bool IsDisposed => _disposed.Task.IsCompleted;

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            if (_position < data.Length)
            {
                var count = Math.Min(buffer.Length, data.Length - _position);
                data.AsMemory(_position, count).CopyTo(buffer);
                _position += count;
                return count;
            }
            if (endAfterData) return 0;

            await _disposed.Task; // belirteç bilinçli olarak kullanılmıyor
            throw new IOException("Port kapatıldı.");
        }

        protected override void Dispose(bool disposing)
        {
            _disposed.TrySetResult();
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
