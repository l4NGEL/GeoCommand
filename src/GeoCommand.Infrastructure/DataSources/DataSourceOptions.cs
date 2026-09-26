namespace GeoCommand.Infrastructure.DataSources;

public sealed class DataSourceOptions
{
    public const string Section = "DataSource";

    /// <summary>"Simulator" veya "File".</summary>
    public string Type { get; set; } = SimulatedPositionSource.TypeName;

    /// <summary>API açılırken kaynağın otomatik başlatılıp başlatılmayacağı.</summary>
    public bool AutoStart { get; set; } = true;

    public SimulatorOptions Simulator { get; set; } = new();
    public FileSourceOptions File { get; set; } = new();
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
