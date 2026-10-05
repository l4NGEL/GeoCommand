using System.Globalization;
using GeoCommand.Grpc.V1;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Grpc.Net.Client;

// GeoCommand gRPC örnek istemcisi.
//
//   geocommand-grpc stream [--file data/replay/ankara-kayit.csv] [--speed 5] [--address http://localhost:5081]
//       Sahadaki birim rolü: kayıt dosyasındaki konumları gönderim anının zaman damgasıyla tek bir
//       StreamPositions akışı üzerinden yollar; bitince (veya Ctrl+C) sunucunun özetini yazdırır.
//
//   geocommand-grpc watch [--callsign ALFA-1 ...] [--snapshot] [--address http://localhost:5081]
//       Dış sistem rolü: OperationsFeed.Subscribe ile canlı araç durumunu ve olayları yazdırır.

Console.OutputEncoding = System.Text.Encoding.UTF8; // Windows konsolunda Türkçe karakterler
CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; // koordinatlarda nokta

var command = args.FirstOrDefault();
var options = ParseOptions(args.Skip(1).ToArray());
var address = options.GetValueOrDefault("address")?.Single() ?? "http://localhost:5081";

using var cancel = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cancel.Cancel(); };

using var channel = GrpcChannel.ForAddress(address);
try
{
    return command switch
    {
        "stream" => await StreamAsync(new Telemetry.TelemetryClient(channel), options, cancel.Token),
        "watch" => await WatchAsync(new OperationsFeed.OperationsFeedClient(channel), options, cancel.Token),
        _ => Usage()
    };
}
catch (RpcException ex) when (ex.StatusCode != StatusCode.Cancelled)
{
    Console.Error.WriteLine($"gRPC hatası: {ex.StatusCode} - {ex.Status.Detail}");
    return 1;
}

static async Task<int> StreamAsync(Telemetry.TelemetryClient client, Dictionary<string, List<string>> options, CancellationToken ct)
{
    var file = options.GetValueOrDefault("file")?.Single() ?? Path.Combine(FindRepoRoot(), "data", "replay", "ankara-kayit.csv");
    var speed = double.Parse(options.GetValueOrDefault("speed")?.Single() ?? "1", CultureInfo.InvariantCulture);
    var rows = ReadReplay(file);
    Console.WriteLine($"{rows.Count} konum, {rows.Select(r => r.Callsign).Distinct().Count()} araç, {speed}x hız");

    // Ctrl+C akışı keser ama özet alınabilsin diye çağrının kendisi iptal edilmez; istemci tarafı tamamlanır.
    using var call = client.StreamPositions();
    var started = DateTimeOffset.UtcNow;
    var sent = 0;
    foreach (var row in rows)
    {
        var due = started + TimeSpan.FromSeconds(row.OffsetSeconds / speed);
        var wait = due - DateTimeOffset.UtcNow;
        try
        {
            if (wait > TimeSpan.Zero) await Task.Delay(wait, ct);
        }
        catch (OperationCanceledException)
        {
            break;
        }

        await call.RequestStream.WriteAsync(new PositionReport
        {
            Callsign = row.Callsign,
            Latitude = row.Latitude,
            Longitude = row.Longitude,
            SpeedMps = row.SpeedMps,
            HeadingDegrees = row.HeadingDegrees,
            Timestamp = Timestamp.FromDateTimeOffset(DateTimeOffset.UtcNow)
        }, CancellationToken.None);
        if (++sent % 50 == 0) Console.WriteLine($"  {sent} gönderildi");
    }
    await call.RequestStream.CompleteAsync();

    var summary = await call;
    Console.WriteLine($"Özet: alınan={summary.Received} kabul={summary.Accepted} geçersiz={summary.Invalid} " +
                      $"tanımsız={summary.UnknownVehicle} yinelenen={summary.Duplicate} çakışma={summary.Conflict}");
    foreach (var r in summary.Rejections)
        Console.WriteLine($"  #{r.Index} {r.Callsign}: {r.Reason} {string.Join(" ", r.Errors)}");
    return 0;
}

static async Task<int> WatchAsync(OperationsFeed.OperationsFeedClient client, Dictionary<string, List<string>> options, CancellationToken ct)
{
    var request = new SubscribeRequest { IncludeSnapshot = options.ContainsKey("snapshot") };
    request.Callsigns.AddRange(options.GetValueOrDefault("callsign") ?? []);
    Console.WriteLine($"Abone olunuyor (filtre: {(request.Callsigns.Count == 0 ? "tümü" : string.Join(", ", request.Callsigns))}). Ctrl+C ile çıkış.");

    using var call = client.Subscribe(request, cancellationToken: ct);
    try
    {
        await foreach (var update in call.ResponseStream.ReadAllAsync(ct))
        {
            var tag = update.Snapshot ? "ANLIK " : "";
            switch (update.UpdateCase)
            {
                case OperationsUpdate.UpdateOneofCase.Vehicle:
                    var v = update.Vehicle;
                    Console.WriteLine(
                        $"{tag}{v.LastUpdate?.ToDateTimeOffset():HH:mm:ss} {v.Callsign,-10} {v.State,-10} {(v.HasLatitude ? $"{v.Latitude:F5},{v.Longitude:F5}" : "konum yok"),-20} {v.SpeedMps,5:F1} m/s");
                    break;
                case OperationsUpdate.UpdateOneofCase.Event:
                    var e = update.Event;
                    Console.WriteLine($"{e.OccurredAt.ToDateTimeOffset():HH:mm:ss} OLAY {e.Type}: {e.Message}");
                    break;
            }
        }
    }
    catch (OperationCanceledException)
    {
    }
    catch (RpcException ex) when (ex.StatusCode == StatusCode.Cancelled)
    {
    }
    return 0;
}

static int Usage()
{
    Console.Error.WriteLine("""
        Kullanım:
          geocommand-grpc stream [--file <csv>] [--speed <kat>] [--address <url>]
          geocommand-grpc watch  [--callsign <ad> ...] [--snapshot] [--address <url>]
        Varsayılan adres: http://localhost:5081 (API'nin HTTP/2 gRPC uç noktası)
        """);
    return 2;
}

static Dictionary<string, List<string>> ParseOptions(string[] args)
{
    var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
    for (var i = 0; i < args.Length; i++)
    {
        if (!args[i].StartsWith("--", StringComparison.Ordinal)) throw new ArgumentException($"Beklenmeyen argüman: {args[i]}");
        var key = args[i][2..];
        var values = result.TryGetValue(key, out var existing) ? existing : result[key] = [];
        if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)) values.Add(args[++i]);
    }
    return result;
}

static List<ReplayRow> ReadReplay(string path)
{
    var rows = new List<ReplayRow>();
    string[]? header = null;
    foreach (var line in File.ReadLines(path))
    {
        if (line.Length == 0 || line.StartsWith('#')) continue;
        var cells = line.Split(',');
        if (header is null) { header = cells; continue; }
        string Cell(string name) => cells[Array.IndexOf(header, name)];
        double Number(string name) => double.Parse(Cell(name), CultureInfo.InvariantCulture);
        rows.Add(new ReplayRow(Number("offset_seconds"), Cell("callsign"), Number("latitude"), Number("longitude"), Number("speed_mps"), Number("heading_deg")));
    }
    return rows.OrderBy(r => r.OffsetSeconds).ToList();
}

static string FindRepoRoot()
{
    for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        if (File.Exists(Path.Combine(dir.FullName, "GeoCommand.slnx"))) return dir.FullName;
    return Directory.GetCurrentDirectory();
}

internal sealed record ReplayRow(double OffsetSeconds, string Callsign, double Latitude, double Longitude, double SpeedMps, double HeadingDegrees);
