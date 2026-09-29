using System.Runtime.CompilerServices;
using GeoCommand.Sdk;
using GeoCommand.Infrastructure.DataSources.Replay;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoCommand.Infrastructure.DataSources;

/// <summary>
/// Önceden kaydedilmiş CSV dosyasını oynatır. Zaman damgaları oynatmanın başladığı ana göre yeniden
/// hesaplanır (başlangıç + offset / hız); böylece eski bir kayıt canlı akış gibi işlenebilir.
/// </summary>
public sealed class FilePositionSource(
    IOptions<DataSourceOptions> options,
    TimeProvider time,
    ILogger<FilePositionSource> logger) : IPositionSource
{
    public const string TypeName = "File";
    public const string ReportSource = "file";

    private FileSourceOptions Options => options.Value.File;
    private string Directory => PathResolver.Resolve(Options.Directory);

    public string SourceType => TypeName;

    public string DefaultScenario => Options.DefaultFile;

    public IReadOnlyList<string> AvailableScenarios =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.GetFiles(Directory, "*.csv").Select(Path.GetFileName).OfType<string>().Order().ToList()
            : [];

    public IReadOnlyList<ReplayRow> Load(string fileName)
    {
        if (!AvailableScenarios.Contains(fileName, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"'{fileName}' adlı kayıt dosyası yok. Mevcut: {string.Join(", ", AvailableScenarios)}.");

        using var reader = new StreamReader(Path.Combine(Directory, fileName));
        return ReplayFile.Parse(reader, fileName);
    }

    public async IAsyncEnumerable<PositionReport> ReadAsync(string scenario, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (Options.PlaybackSpeed is <= 0 or > 100)
            throw new InvalidOperationException("DataSource:File:PlaybackSpeed 0 ile 100 arasında olmalı.");

        var rows = Load(scenario);
        var pass = 0;
        do
        {
            pass++;
            var start = time.GetUtcNow();
            logger.LogInformation("Kayıt oynatma başladı. Dosya={File} Satır={RowCount} Hız={Speed}x Tur={Pass}",
                scenario, rows.Count, Options.PlaybackSpeed, pass);

            foreach (var row in rows)
            {
                var due = start + TimeSpan.FromSeconds(row.OffsetSeconds / Options.PlaybackSpeed);
                var wait = due - time.GetUtcNow();
                if (wait > TimeSpan.Zero) await Task.Delay(wait, time, cancellationToken);

                yield return new PositionReport(row.Callsign, row.Latitude, row.Longitude, row.SpeedMps, row.HeadingDegrees, due, ReportSource);
            }

            // Yeni turun zaman damgaları bir öncekinin son bildiriminden kesinlikle sonra olsun.
            await Task.Delay(TimeSpan.FromSeconds(1), time, cancellationToken);
        }
        while (Options.Loop && !cancellationToken.IsCancellationRequested);

        logger.LogInformation("Kayıt oynatma bitti. Dosya={File}", scenario);
    }
}
