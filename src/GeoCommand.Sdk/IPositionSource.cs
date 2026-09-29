namespace GeoCommand.Sdk;

/// <summary>Kaynaktan bağımsız ham konum bildirimi. Doğrulama API'deki ortak alma hattında yapılır.</summary>
/// <param name="Source">Bildirimin geldiği kaynak etiketi (ör. "simulator", "nmea"); konum geçmişinde saklanır.</param>
public sealed record PositionReport(
    string Callsign,
    double Latitude,
    double Longitude,
    double SpeedMps,
    double HeadingDegrees,
    DateTimeOffset Timestamp,
    string Source);

/// <summary>
/// Konum verisi üreten kaynak. Yerleşik simülatör ve kayıt dosyası oynatıcısı ile <c>plugins/</c> klasöründen
/// yüklenen kaynaklar bu arayüzü uygular; hangisinin çalışacağı <c>DataSource:Type</c> ile veya çalışma anında
/// API'den seçilir.
/// </summary>
public interface IPositionSource
{
    /// <summary>Kaynağın tekil adı ("Simulator", "File", "Nmea" …). Plugin'lerde <see cref="PositionSourceAttribute.Name"/> ile aynı olmalı.</summary>
    string SourceType { get; }

    /// <summary>Seçilebilir senaryolar: simülatör için senaryo adları, dosya kaynağı için kayıt dosyaları vb.</summary>
    IReadOnlyList<string> AvailableScenarios { get; }

    string DefaultScenario { get; }

    /// <summary>İptal edilene veya senaryo bitene kadar bildirim üretir.</summary>
    IAsyncEnumerable<PositionReport> ReadAsync(string scenario, CancellationToken cancellationToken);
}
