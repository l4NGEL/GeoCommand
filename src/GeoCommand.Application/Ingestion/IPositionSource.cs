namespace GeoCommand.Application.Ingestion;

/// <summary>Kaynaktan bağımsız ham konum bildirimi. Doğrulama <see cref="PositionIngestionService"/>'te yapılır.</summary>
public sealed record PositionReport(
    string Callsign,
    double Latitude,
    double Longitude,
    double SpeedMps,
    double HeadingDegrees,
    DateTimeOffset Timestamp,
    string Source);

/// <summary>
/// Konum verisi üreten kaynak. Simülatör ve kayıt dosyası oynatıcısı bu arayüzü uygular;
/// hangisinin kullanılacağı <c>DataSource:Type</c> yapılandırmasıyla seçilir.
/// </summary>
public interface IPositionSource
{
    /// <summary>Yapılandırmada kullanılan ad ("Simulator" veya "File").</summary>
    string SourceType { get; }

    /// <summary>Seçilebilir senaryolar: simülatör için senaryo adları, dosya kaynağı için kayıt dosyaları.</summary>
    IReadOnlyList<string> AvailableScenarios { get; }

    string DefaultScenario { get; }

    /// <summary>İptal edilene veya senaryo bitene kadar bildirim üretir.</summary>
    IAsyncEnumerable<PositionReport> ReadAsync(string scenario, CancellationToken cancellationToken);
}
