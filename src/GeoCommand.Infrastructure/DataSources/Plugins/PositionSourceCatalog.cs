using System.Composition.Hosting;
using System.Reflection;
using GeoCommand.Sdk;
using Microsoft.Extensions.Logging;

namespace GeoCommand.Infrastructure.DataSources.Plugins;

public enum PositionSourceOrigin
{
    BuiltIn,
    Plugin
}

/// <summary>Katalogdaki bir kaynak. Kaynak örneği ilk kullanımda oluşturulur; seçilmeyen plugin'ler hiç örneklenmez.</summary>
public sealed class PositionSourceEntry
{
    private readonly Lazy<IPositionSource> _instance;

    public PositionSourceEntry(string name, string description, PositionSourceOrigin origin, string? version, string? location, Func<IPositionSource> factory)
    {
        Name = name;
        Description = description;
        Origin = origin;
        Version = version;
        Location = location;
        _instance = new Lazy<IPositionSource>(() =>
        {
            var source = factory();
            if (!string.Equals(source.SourceType, name, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"'{name}' olarak dışa aktarılan kaynağın SourceType değeri '{source.SourceType}'. İkisi aynı olmalı.");
            return source;
        });
    }

    public string Name { get; }
    public string Description { get; }
    public PositionSourceOrigin Origin { get; }

    /// <summary>Plugin derlemesinin sürümü; yerleşik kaynaklarda null.</summary>
    public string? Version { get; }

    /// <summary>Plugin DLL'inin yolu; yerleşik kaynaklarda null.</summary>
    public string? Location { get; }

    public IPositionSource Source => _instance.Value;
}

/// <summary>
/// Yerleşik kaynaklar ile plugin klasöründen MEF (<c>System.Composition</c>) aracılığıyla keşfedilen kaynakların listesi.
/// <para>
/// Klasör düzeni: <c>&lt;plugins&gt;/&lt;Ad&gt;/&lt;Ad&gt;.dll</c> (+ <c>.deps.json</c> ve plugin'in kendi bağımlılıkları).
/// Her plugin ayrı bir <see cref="PluginLoadContext"/>'e ve ayrı bir MEF kapsayıcısına yüklenir; bozuk bir plugin
/// yalnızca kendisinin atlanmasına yol açar, API açılmaya devam eder ve hata <see cref="LoadErrors"/>'da görünür.
/// </para>
/// </summary>
public sealed class PositionSourceCatalog : IDisposable
{
    private readonly List<PositionSourceEntry> _entries = [];
    private readonly List<string> _loadErrors = [];
    private readonly List<CompositionHost> _containers = [];

    private PositionSourceCatalog() { }

    public IReadOnlyList<PositionSourceEntry> Entries => _entries;

    public IReadOnlyList<string> LoadErrors => _loadErrors;

    public PositionSourceEntry? Find(string name) =>
        _entries.FirstOrDefault(e => string.Equals(e.Name, name, StringComparison.OrdinalIgnoreCase));

    public static PositionSourceCatalog Create(
        IEnumerable<PositionSourceEntry> builtIns,
        string? pluginDirectory,
        IPositionSourceHost host,
        ILogger logger)
    {
        var catalog = new PositionSourceCatalog();
        foreach (var entry in builtIns) catalog.Add(entry, logger);

        if (string.IsNullOrWhiteSpace(pluginDirectory))
        {
            logger.LogInformation("Plugin yükleme kapalı.");
        }
        else if (!Directory.Exists(pluginDirectory))
        {
            logger.LogInformation("Plugin klasörü yok, yalnızca yerleşik kaynaklar kullanılacak. Klasör={Directory}", pluginDirectory);
        }
        else
        {
            foreach (var directory in Directory.GetDirectories(pluginDirectory).Order(StringComparer.OrdinalIgnoreCase))
                catalog.LoadPlugin(directory, host, logger);
        }

        logger.LogInformation("Veri kaynakları: {Sources}",
            string.Join(", ", catalog._entries.Select(e => e.Origin == PositionSourceOrigin.Plugin ? $"{e.Name} (plugin {e.Version})" : e.Name)));
        return catalog;
    }

    private void LoadPlugin(string directory, IPositionSourceHost host, ILogger logger)
    {
        var name = Path.GetFileName(directory);
        var assemblyPath = Path.Combine(directory, name + ".dll");
        if (!File.Exists(assemblyPath))
        {
            Fail(logger, null, $"{name}: '{name}.dll' bulunamadı (beklenen düzen: plugins/<Ad>/<Ad>.dll).");
            return;
        }

        CompositionHost? container = null;
        try
        {
            var assembly = new PluginLoadContext(assemblyPath).LoadFromAssemblyPath(assemblyPath);
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
                          ?? assembly.GetName().Version?.ToString();

            container = new ContainerConfiguration()
                .WithAssembly(assembly)
                .WithExport(host)
                .CreateContainer();

            // Lazy + meta veri: plugin sınıfı burada örneklenmez, yalnızca [PositionSource] özniteliği okunur.
            var exports = container.GetExports<Lazy<IPositionSource, PositionSourceMetadata>>().ToList();
            if (exports.Count == 0)
            {
                Fail(logger, null, $"{name}: [PositionSource] ile dışa aktarılmış bir IPositionSource yok.");
                container.Dispose();
                return;
            }

            foreach (var export in exports)
            {
                if (string.IsNullOrWhiteSpace(export.Metadata.Name))
                {
                    Fail(logger, null, $"{name}: [PositionSource] adı boş.");
                    continue;
                }
                Add(new PositionSourceEntry(export.Metadata.Name, export.Metadata.Description, PositionSourceOrigin.Plugin,
                    version, assemblyPath, () => export.Value), logger);
            }
            _containers.Add(container);
        }
        catch (Exception ex)
        {
            container?.Dispose();
            Fail(logger, ex, $"{name}: plugin yüklenemedi ({ex.GetType().Name}: {ex.Message}).");
        }
    }

    private void Add(PositionSourceEntry entry, ILogger logger)
    {
        if (Find(entry.Name) is { } existing)
        {
            Fail(logger, null, $"'{entry.Name}' adlı kaynak zaten var ({existing.Location ?? "yerleşik"}); {entry.Location} atlandı.");
            return;
        }
        _entries.Add(entry);
    }

    private void Fail(ILogger logger, Exception? ex, string message)
    {
        _loadErrors.Add(message);
        logger.LogError(ex, "Plugin hatası: {Message}", message);
    }

    public void Dispose()
    {
        foreach (var container in _containers) container.Dispose();
        _containers.Clear();
    }
}
