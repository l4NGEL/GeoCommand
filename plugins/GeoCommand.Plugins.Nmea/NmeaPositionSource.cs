using System.Composition;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using GeoCommand.Sdk;
using Microsoft.Extensions.Logging;

namespace GeoCommand.Plugins.Nmea;

/// <summary>
/// GPS alıcılarından NMEA 0183 <c>$xxRMC</c> cümlelerini okur: RS-232/USB-seri port üzerinden veya TCP üzerinden
/// (NMEA-over-TCP ağ geçitleri; 10110 standart porttur). Bir senaryo, her biri bir araca ait bir veya daha çok akıştan
/// oluşur; akışlar paralel okunur ve aynı senaryoda seri ve TCP akışları karışık olabilir. Bağlantı düşerse
/// (kablo çekildi, port başka uygulamada, ağ geçidi kapandı) <c>ReconnectSeconds</c> sonra yeniden denenir; bir akışın
/// hatası diğerlerini durdurmaz.
/// </summary>
[PositionSource(TypeName, Description = "NMEA 0183 ($xxRMC) GPS akışı, RS-232 seri port veya TCP üzerinden")]
public sealed class NmeaPositionSource : IPositionSource
{
    public const string TypeName = "Nmea";
    public const string ReportSource = "nmea";

    private readonly IPositionSourceHost _host;
    private readonly INmeaConnector _connector;
    private readonly ILogger _logger;

    [ImportingConstructor]
    public NmeaPositionSource(IPositionSourceHost host) : this(host, NmeaConnector.Instance) { }

    /// <summary>Testler için: gerçek seri port veya soket yerine sahte bağlantı.</summary>
    internal NmeaPositionSource(IPositionSourceHost host, INmeaConnector connector)
    {
        _host = host;
        _connector = connector;
        _logger = host.LoggerFactory.CreateLogger<NmeaPositionSource>();
    }

    // Her çağrıda yeniden okunur: yapılandırma dosyası değişirse API'yi yeniden başlatmak gerekmez.
    private NmeaSettings Settings => NmeaSettings.Read(_host.GetSettings(TypeName));

    public string SourceType => TypeName;

    public IReadOnlyList<string> AvailableScenarios => Settings.Scenarios.Keys.Order(StringComparer.OrdinalIgnoreCase).ToList();

    public string DefaultScenario => Settings.DefaultScenario;

    public async IAsyncEnumerable<PositionReport> ReadAsync(string scenario, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var settings = Settings;
        if (!settings.Scenarios.TryGetValue(scenario, out var streams))
            throw new ArgumentException($"'{scenario}' adlı NMEA senaryosu yok. Mevcut: {string.Join(", ", settings.Scenarios.Keys)}.");

        _logger.LogInformation("NMEA kaynağı başladı. Senaryo={Scenario} Akışlar={Streams}", scenario, string.Join("; ", streams));

        // Sınırlı kanal: tüketici (veritabanı) yavaşlarsa soket okuması da yavaşlar, bellek büyümez.
        var channel = Channel.CreateBounded<PositionReport>(new BoundedChannelOptions(256) { SingleReader = true });
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var readers = streams.Select(s => RunStreamAsync(s, settings.ReconnectDelay, channel.Writer, stop.Token)).ToArray();
        _ = Task.WhenAll(readers).ContinueWith(_ => channel.Writer.TryComplete(), TaskScheduler.Default);

        try
        {
            await foreach (var report in channel.Reader.ReadAllAsync(cancellationToken))
                yield return report;
        }
        finally
        {
            await stop.CancelAsync();
            await Task.WhenAll(readers);
        }
    }

    /// <summary>
    /// Yeniden denenemeyen bir hata (ör. işletim sisteminin kabul etmediği port adı) kaynağın tamamını durdurur:
    /// kanal hatayla kapatılır, hata API'deki kaynak durumunda görünür. Diğer akışlar iptal edilir.
    /// </summary>
    private async Task RunStreamAsync(NmeaStream stream, TimeSpan reconnectDelay, ChannelWriter<PositionReport> writer, CancellationToken cancellationToken)
    {
        try
        {
            await ReadStreamAsync(stream, reconnectDelay, writer, cancellationToken);
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            _logger.LogError(ex, "NMEA akışı yeniden denenemeyen bir hatayla durdu. {Stream}", stream);
            writer.TryComplete(new InvalidOperationException($"{stream}: {ex.Message}", ex));
        }
        catch (Exception ex) when (cancellationToken.IsCancellationRequested || ex is ChannelClosedException)
        {
            // Durduruldu ya da başka bir akış kanalı hatayla kapattı.
        }
    }

    private async Task ReadStreamAsync(NmeaStream stream, TimeSpan reconnectDelay, ChannelWriter<PositionReport> writer, CancellationToken cancellationToken)
    {
        var lastCourse = 0.0;
        while (!cancellationToken.IsCancellationRequested)
        {
            long accepted = 0, noFix = 0, invalid = 0;
            try
            {
                await using var connection = await _connector.ConnectAsync(stream.Endpoint, cancellationToken);
                // Seri portun bekleyen okuması iptal belirtecini dinlemez; iptalde akışı kapatmak okumayı da bitirir.
                await using var abort = cancellationToken.Register(static c => ((Stream)c!).Dispose(), connection);
                _logger.LogInformation("NMEA akışına bağlanıldı. {Stream}", stream);

                var reader = new NmeaLineReader(connection);
                while (await reader.ReadLineAsync(cancellationToken) is { } line)
                {
                    if (line.Length == 0) continue;
                    switch (RmcSentence.TryParse(line, out var fix, out var error))
                    {
                        case RmcParseStatus.Ok:
                            // Duran alıcı rota bildirmez; harita okunun zıplamaması için son bilinen rota korunur.
                            lastCourse = fix!.CourseDegrees ?? lastCourse;
                            await writer.WriteAsync(new PositionReport(
                                stream.Callsign, fix.Latitude, fix.Longitude, fix.SpeedMps, lastCourse, fix.TimestampUtc, ReportSource), cancellationToken);
                            accepted++;
                            break;
                        case RmcParseStatus.NoFix:
                            noFix++;
                            break;
                        case RmcParseStatus.Invalid:
                            // İlk bozuk cümle uyarı olarak, sonrakiler ayrıntı düzeyinde loglanır (bozuk hat logu doldurmasın).
                            if (invalid++ == 0) _logger.LogWarning("Geçersiz NMEA cümlesi. {Stream} Hata={Error} Cümle={Line}", stream, error, line);
                            else _logger.LogDebug("Geçersiz NMEA cümlesi. {Stream} Hata={Error}", stream, error);
                            break;
                    }
                }
                _logger.LogWarning("NMEA akışı karşı taraftan kapatıldı. {Stream} Kabul={Accepted} KonumYok={NoFix} Geçersiz={Invalid} UzunSatır={Discarded}",
                    stream, accepted, noFix, invalid, reader.DiscardedLines);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
                // İptalde kapatılan akış OperationCanceledException yerine ObjectDisposedException/IOException fırlatabilir.
                break;
            }
            catch (Exception ex) when (ex is SocketException or IOException)
            {
                _logger.LogWarning("NMEA akışına bağlanılamadı veya bağlantı koptu. {Stream} Hata={Error} Kabul={Accepted}; {Delay} sn sonra yeniden denenecek.",
                    stream, ex.Message, accepted, reconnectDelay.TotalSeconds);
            }

            try
            {
                await Task.Delay(reconnectDelay, _host.Time, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
