namespace GeoCommand.Infrastructure.DataSources;

public sealed class DataSourceOptions
{
    public const string Section = "DataSource";

    /// <summary>Açılışta seçili kaynak: "Simulator", "File" veya yüklenen bir plugin'in adı (ör. "Nmea").</summary>
    public string Type { get; set; } = SimulatedPositionSource.TypeName;

    /// <summary>API açılırken kaynağın otomatik başlatılıp başlatılmayacağı.</summary>
    public bool AutoStart { get; set; } = true;

    public SimulatorOptions Simulator { get; set; } = new();
    public FileSourceOptions File { get; set; } = new();
}

/// <summary>
/// <c>DataSource:Plugins</c>. Katalog, kaynak seçimini doğrulayan <see cref="DataSourceOptions"/>'tan önce oluşturulduğu
/// için ayrı bir seçenek sınıfıdır.
/// </summary>
public sealed class PluginOptions
{
    public const string Section = DataSourceOptions.Section + ":Plugins";

    /// <summary>Plugin klasörü; göreli yollar uygulamanın içerik köküne (content root) göre çözülür. Boş: plugin yükleme kapalı.</summary>
    public string? Directory { get; set; } = "plugins";
}

public sealed class SimulatorOptions
{
    /// <summary>Uygulama dizinine göre veya mutlak yol.</summary>
    public string ScenarioDirectory { get; set; } = "data/scenarios";
    public string DefaultScenario { get; set; } = "ankara-devriye";
}

public sealed class FileSourceOptions
{
    public string Directory { get; set; } = "data/replay";
    public string DefaultFile { get; set; } = "ankara-kayit.csv";

    /// <summary>1 = gerçek zamanlı, 2 = iki kat hızlı.</summary>
    public double PlaybackSpeed { get; set; } = 1.0;

    /// <summary>Dosya bittiğinde baştan oynat.</summary>
    public bool Loop { get; set; } = true;
}

internal static class PathResolver
{
    public static string Resolve(string path) =>
        Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, path));
}
