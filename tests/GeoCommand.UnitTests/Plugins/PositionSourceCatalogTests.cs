using System.Net;
using System.Net.Sockets;
using System.Runtime.Loader;
using System.Text;
using GeoCommand.Infrastructure.DataSources.Plugins;
using GeoCommand.Plugins.Nmea;
using GeoCommand.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace GeoCommand.UnitTests.Plugins;

/// <summary>
/// Gerçek plugin derlemesini (artifacts/plugins, test projesinin derlemesi sırasında üretilir) geçici bir klasöre
/// kopyalayıp katalogla yükler. Böylece API'nin açılışta yaptığı keşif birebir sınanır.
/// </summary>
public sealed class PositionSourceCatalogTests : IDisposable
{
    private const string PluginName = "GeoCommand.Plugins.Nmea";
    private readonly DirectoryInfo _root = Directory.CreateTempSubdirectory("geocommand-plugins-");

    public PositionSourceCatalogTests()
    {
        var built = Path.Combine(RepoPaths.Root, "artifacts", "plugins", PluginName);
        Assert.True(Directory.Exists(built), $"Plugin derleme çıktısı yok: {built}");
        // runtimes/ alt klasörleri dahil: System.IO.Ports'un platforma özgü derlemesi oradan çözülür.
        foreach (var file in Directory.GetFiles(built, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(_root.FullName, PluginName, Path.GetRelativePath(built, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    public void Dispose()
    {
        try { _root.Delete(recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { } // yüklü DLL Windows'ta kilitli kalır
    }

    private static PositionSourceHost Host(params (string Key, string Value)[] settings) =>
        new(new ConfigurationBuilder()
                .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
                .Build(),
            NullLoggerFactory.Instance,
            TimeProvider.System);

    private PositionSourceCatalog Load(IPositionSourceHost? host = null, params PositionSourceEntry[] builtIns) =>
        PositionSourceCatalog.Create(builtIns, _root.FullName, host ?? Host(), NullLogger.Instance);

    [Fact]
    public void Plugin_is_discovered_through_mef_with_metadata_and_version()
    {
        using var catalog = Load();

        var entry = Assert.Single(catalog.Entries);
        Assert.Equal("Nmea", entry.Name);
        Assert.Equal(PositionSourceOrigin.Plugin, entry.Origin);
        Assert.Contains("NMEA 0183", entry.Description);
        Assert.Equal("1.1.0", entry.Version);
        Assert.Equal(Path.Combine(_root.FullName, PluginName, PluginName + ".dll"), entry.Location);
        Assert.Empty(catalog.LoadErrors);
    }

    [Fact]
    public void Plugin_runs_in_its_own_load_context_but_shares_the_sdk_contract()
    {
        using var catalog = Load();

        var source = catalog.Find("nmea")!.Source; // ad büyük/küçük harf duyarsız
        var pluginAssembly = source.GetType().Assembly;

        // Test projesi aynı plugin'e doğrudan da başvurur; yine de katalogdaki kopya ayrı bağlamdan gelir.
        Assert.Equal("PluginLoadContext", AssemblyLoadContext.GetLoadContext(pluginAssembly)!.GetType().Name);
        Assert.NotSame(typeof(NmeaPositionSource), source.GetType());
        // Sözleşme paylaşıldığı için ayrı bağlamdaki tür yine API'nin IPositionSource'u olarak kullanılabilir.
        Assert.IsAssignableFrom<IPositionSource>(source);
        Assert.Same(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(typeof(IPositionSource).Assembly));
    }

    [Fact]
    public void Broken_plugins_are_skipped_and_reported_while_valid_ones_still_load()
    {
        var garbage = Directory.CreateDirectory(Path.Combine(_root.FullName, "Bozuk"));
        File.WriteAllText(Path.Combine(garbage.FullName, "Bozuk.dll"), "bu bir .NET derlemesi değil");
        Directory.CreateDirectory(Path.Combine(_root.FullName, "DllYok"));

        using var catalog = Load();

        Assert.Equal(["Nmea"], catalog.Entries.Select(e => e.Name));
        Assert.Equal(2, catalog.LoadErrors.Count);
        Assert.Contains(catalog.LoadErrors, e => e.StartsWith("Bozuk:") && e.Contains("BadImageFormatException"));
        Assert.Contains(catalog.LoadErrors, e => e.StartsWith("DllYok:") && e.Contains("bulunamadı"));
    }

    [Fact]
    public void Built_in_sources_win_name_conflicts()
    {
        var builtIn = new PositionSourceEntry("Nmea", "yerleşik", PositionSourceOrigin.BuiltIn, null, null,
            () => throw new InvalidOperationException("örneklenmemeli"));

        using var catalog = Load(null, builtIn);

        Assert.Same(builtIn, Assert.Single(catalog.Entries));
        Assert.Contains("zaten var", Assert.Single(catalog.LoadErrors));
    }

    [Fact]
    public void Missing_plugin_directory_leaves_only_built_in_sources()
    {
        var builtIn = new PositionSourceEntry("Simulator", "", PositionSourceOrigin.BuiltIn, null, null, () => null!);

        using var catalog = PositionSourceCatalog.Create([builtIn], Path.Combine(_root.FullName, "yok"), Host(), NullLogger.Instance);

        Assert.Same(builtIn, Assert.Single(catalog.Entries));
        Assert.Empty(catalog.LoadErrors);
    }

    [Fact]
    public async Task Loaded_plugin_reads_rmc_sentences_from_a_tcp_receiver()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var at = new DateTimeOffset(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

        var server = Task.Run(async () =>
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            var lines = new[]
            {
                RmcSentence.Format(new RmcFix(at, 39.92, 32.85, 10, 90)),
                "$GPRMC,120001.00,A,3955.2000,N,03251.0000,E,19.4,90.0,280926,,,A*00", // bozuk sağlama toplamı: atlanır
                "$GPRMC,120001.00,V,,,,,,,280926,,,N*" + RmcSentence.Checksum("GPRMC,120001.00,V,,,,,,,280926,,,N").ToString("X2"),
                RmcSentence.Format(new RmcFix(at.AddSeconds(2), 39.921, 32.851, 0, null)) // duran alıcı: rota boş
            };
            await stream.WriteAsync(Encoding.ASCII.GetBytes(string.Join("\r\n", lines) + "\r\n"));
            await Task.Delay(TimeSpan.FromSeconds(10)); // bağlantıyı açık tut
        });

        using var catalog = Load(Host(
            ("DataSource:Nmea:ReconnectSeconds", "0.2"),
            ("DataSource:Nmea:Scenarios:test:0:Callsign", "ALFA-1"),
            ("DataSource:Nmea:Scenarios:test:0:Host", "127.0.0.1"),
            ("DataSource:Nmea:Scenarios:test:0:Port", port.ToString())));
        var source = catalog.Find("Nmea")!.Source;
        Assert.Equal(["test"], source.AvailableScenarios);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var reports = new List<PositionReport>();
        await foreach (var report in source.ReadAsync("test", timeout.Token))
        {
            reports.Add(report);
            if (reports.Count == 2) break;
        }

        Assert.Equal(("ALFA-1", at, "nmea", 90.0), (reports[0].Callsign, reports[0].Timestamp, reports[0].Source, reports[0].HeadingDegrees));
        Assert.Equal(39.92, reports[0].Latitude, 6);
        Assert.Equal(32.85, reports[0].Longitude, 6);
        Assert.Equal(10, reports[0].SpeedMps, 1);
        Assert.Equal(at.AddSeconds(2), reports[1].Timestamp);
        Assert.Equal(90, reports[1].HeadingDegrees); // son bilinen rota korunur
        Assert.Equal(0, reports[1].SpeedMps);
    }

    [Fact]
    public async Task Loaded_plugin_resolves_its_own_platform_specific_serial_port_dependency()
    {
        // Var olmayan port: bağlantı her denemede yeniden denenebilir bir IOException ile başarısız olur. Plugin
        // System.IO.Ports'un platformdan bağımsız "desteklenmiyor" kopyasını yükleseydi PlatformNotSupportedException
        // yeniden denenemeyen hata olarak ReadAsync'ten fırlardı.
        using var catalog = Load(Host(
            ("DataSource:Nmea:ReconnectSeconds", "0.1"),
            ("DataSource:Nmea:Scenarios:seri:0:Callsign", "ALFA-1"),
            ("DataSource:Nmea:Scenarios:seri:0:SerialPort", OperatingSystem.IsWindows() ? "COM250" : "/dev/ttyGEOCOMMAND250")));
        var source = catalog.Find("Nmea")!.Source;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await foreach (var _ in source.ReadAsync("seri", timeout.Token)) Assert.Fail("konum gelmemeliydi");
        });

        var ports = AssemblyLoadContext.GetLoadContext(source.GetType().Assembly)!.Assemblies
            .Single(a => a.GetName().Name == "System.IO.Ports");
        Assert.NotSame(AssemblyLoadContext.Default, AssemblyLoadContext.GetLoadContext(ports)); // API bu pakete hiç başvurmaz
        var expected = Path.Combine("runtimes", OperatingSystem.IsWindows() ? "win" : "unix", "lib");
        Assert.Contains(expected, ports.Location);
    }
}
