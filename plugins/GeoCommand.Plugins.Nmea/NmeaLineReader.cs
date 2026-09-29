using System.Text;

namespace GeoCommand.Plugins.Nmea;

/// <summary>
/// Bayt akışını satırlara böler (CR/LF veya LF). <see cref="StreamReader.ReadLineAsync()"/>'in aksine satır uzunluğu
/// sınırlıdır: sonu gelmeyen veya çok uzun bir satır belleği şişiremez, atlanır ve <see cref="DiscardedLines"/> artar.
/// Seri hat ve TCP için aynı sınıf kullanılır.
/// </summary>
public sealed class NmeaLineReader(Stream stream, int maxLineLength = RmcSentence.MaxLength)
{
    private readonly byte[] _buffer = new byte[4096];
    private readonly StringBuilder _line = new();
    private int _position;
    private int _count;
    private bool _discarding;

    public long DiscardedLines { get; private set; }

    /// <summary>Sonraki satırı döndürür; akış kapandıysa null.</summary>
    public async ValueTask<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            while (_position < _count)
            {
                var b = _buffer[_position++];
                if (b == (byte)'\n')
                {
                    var wasDiscarding = _discarding;
                    _discarding = false;
                    if (wasDiscarding) continue;

                    var line = _line.ToString().TrimEnd('\r');
                    _line.Clear();
                    return line;
                }
                if (_discarding) continue;
                if (_line.Length >= maxLineLength)
                {
                    _line.Clear();
                    _discarding = true;
                    DiscardedLines++;
                    continue;
                }
                // Latin-1: her bayt tek karaktere karşılık gelir, sağlama toplamı ham bayt üzerinden doğru hesaplanır.
                _line.Append((char)b);
            }

            _count = await stream.ReadAsync(_buffer, cancellationToken);
            _position = 0;
            if (_count == 0) return null;
        }
    }
}
