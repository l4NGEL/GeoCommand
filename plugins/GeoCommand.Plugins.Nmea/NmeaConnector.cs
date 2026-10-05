using System.IO.Ports;
using System.Net.Sockets;

namespace GeoCommand.Plugins.Nmea;

/// <summary>
/// Bir <see cref="NmeaEndpoint"/>'e bağlanıp okunabilir bir bayt akışı açar. Döndürülen akış bağlantının sahibidir:
/// kapatılması soketi veya seri portu da kapatır. Testler donanım ya da ağ olmadan sahte bir uygulama verir.
/// </summary>
internal interface INmeaConnector
{
    /// <exception cref="IOException">Port yok, kullanımda veya bağlantı kurulamadı (yeniden denenebilir).</exception>
    /// <exception cref="SocketException">TCP bağlantısı kurulamadı (yeniden denenebilir).</exception>
    Task<Stream> ConnectAsync(NmeaEndpoint endpoint, CancellationToken cancellationToken);
}

internal sealed class NmeaConnector : INmeaConnector
{
    public static readonly NmeaConnector Instance = new();

    public async Task<Stream> ConnectAsync(NmeaEndpoint endpoint, CancellationToken cancellationToken)
    {
        switch (endpoint)
        {
            case TcpEndpoint tcp:
                var socket = new Socket(SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    await socket.ConnectAsync(tcp.Host, tcp.Port, cancellationToken);
                    return new NetworkStream(socket, ownsSocket: true);
                }
                catch
                {
                    socket.Dispose();
                    throw;
                }

            case SerialEndpoint serial:
                return OpenSerial(serial);

            default:
                throw new NotSupportedException($"Desteklenmeyen NMEA uç noktası: {endpoint.GetType().Name}.");
        }
    }

    private static SerialPortStream OpenSerial(SerialEndpoint endpoint)
    {
        var port = new SerialPort(endpoint.PortName, endpoint.BaudRate, endpoint.Parity, endpoint.DataBits, endpoint.StopBits)
        {
            Handshake = Handshake.None,
            // NMEA cihazı veri gönderirken DTR/RTS beklemez; bazı USB-seri dönüştürücüler ise bunlar olmadan güç vermez.
            DtrEnable = true,
            RtsEnable = true
        };
        try
        {
            port.Open();
            port.DiscardInBuffer(); // port açılmadan önce tamponda kalmış yarım cümleler atılır
            return new SerialPortStream(port);
        }
        catch (UnauthorizedAccessException ex)
        {
            port.Dispose();
            // Windows'ta port başka bir uygulama tarafından açıksa; yeniden denenebilir bir hata olarak raporlanır.
            throw new IOException($"{endpoint.PortName} başka bir uygulama tarafından kullanılıyor.", ex);
        }
        catch
        {
            port.Dispose();
            throw;
        }
    }

    /// <summary>
    /// <see cref="SerialPort.BaseStream"/>'i saran ve kapatıldığında portu da kapatan salt okunur akış.
    /// <para>
    /// Windows'ta seri portun <c>ReadAsync</c>'i iptal belirtecini yalnızca başlangıçta denetler; bekleyen bir okuma ancak
    /// port kapatılınca biter. Bu yüzden okuyucu iptalde akışı kapatır (bkz. <see cref="NmeaPositionSource"/>).
    /// </para>
    /// </summary>
    private sealed class SerialPortStream(SerialPort port) : Stream
    {
        private readonly Stream _inner = port.BaseStream;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _inner.ReadAsync(buffer, cancellationToken);

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            _inner.ReadAsync(buffer, offset, count, cancellationToken);

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        protected override void Dispose(bool disposing)
        {
            if (disposing) port.Dispose();
            base.Dispose(disposing);
        }
    }
}
