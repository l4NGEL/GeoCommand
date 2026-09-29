using System.Net;
using System.Net.Sockets;
using System.Text;
using GeoCommand.Contracts;
using GeoCommand.IntegrationTests.Infrastructure;
using GeoCommand.Infrastructure.Persistence;
using GeoCommand.Plugins.Nmea;

namespace GeoCommand.IntegrationTests;

/// <summary>
/// API'yi gerçek plugin klasörüyle (artifacts/plugins) açar. Test, GPS alıcısı yerine bir TCP sunucusu kurar;
/// NMEA plugin'i buna bağlanır ve konumlar simülatörle aynı alma hattından PostGIS'e yazılır.
/// </summary>
public sealed class PluginSourceFactory : GeoCommandApiFactory
{
    /// <summary>Test süreci boyunca açık kalan sahte GPS alıcısı (rastgele boş port).</summary>
    public TcpListener Receiver { get; } = new(IPAddress.Loopback, 0);

    public PluginSourceFactory() => Receiver.Start();

    protected override IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>
    {
        ["DataSource:Plugins:Directory"] = Path.Combine(RepoRoot, "artifacts", "plugins"),
        ["DataSource:Nmea:DefaultScenario"] = "test-alici",
        ["DataSource:Nmea:ReconnectSeconds"] = "0.2",
        ["DataSource:Nmea:Scenarios:test-alici:0:Callsign"] = "DELTA-4",
        ["DataSource:Nmea:Scenarios:test-alici:0:Host"] = "127.0.0.1",
        ["DataSource:Nmea:Scenarios:test-alici:0:Port"] = ((IPEndPoint)Receiver.LocalEndpoint).Port.ToString()
    };

    private static string RepoRoot
    {
        get
        {
            for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "GeoCommand.slnx"))) return dir.FullName;
            throw new InvalidOperationException("Depo kökü (GeoCommand.slnx) bulunamadı.");
        }
    }
}

public sealed class PluginSourceTests(PluginSourceFactory factory) : IClassFixture<PluginSourceFactory>
{
    [Fact]
    public async Task Api_lists_built_in_and_plugin_sources()
    {
        var types = await (await factory.CreateClient().GetAsync("/api/source/types")).ReadAsync<List<SourceTypeDto>>();

        Assert.Equal(["Simulator", "File", "Nmea"], types.Select(t => t.Name));
        var nmea = types.Single(t => t.Name == "Nmea");
        Assert.Equal("Plugin", nmea.Origin);
        Assert.Equal("1.0.0", nmea.Version);
        Assert.Contains("test-alici", nmea.AvailableScenarios); // appsettings.json'daki örnek senaryo da birleşir
        Assert.Equal("test-alici", nmea.DefaultScenario);
        Assert.All(types.Where(t => t.Name != "Nmea"), t => Assert.Equal("BuiltIn", t.Origin));
    }

    [Fact]
    public async Task Switching_to_the_plugin_at_runtime_feeds_nmea_positions_into_the_same_pipeline()
    {
        var client = factory.CreateClient();
        var start = DateTimeOffset.UtcNow;

        var sender = Task.Run(async () =>
        {
            using var gps = await factory.Receiver.AcceptTcpClientAsync();
            await using var stream = gps.GetStream();
            for (var i = 0; i < 5; i++)
            {
                var fix = new RmcFix(start.AddSeconds(i - 10), 39.93 + i * 0.0005, 32.86, 8, 0);
                await stream.WriteAsync(Encoding.ASCII.GetBytes(RmcSentence.Format(fix) + "\r\n"));
            }
            await stream.WriteAsync("$GPRMC,bozuk*00\r\n"u8.ToArray());
            await Task.Delay(TimeSpan.FromSeconds(15));
        });

        // Açılışta simülatör seçili (AutoStart kapalı); istek kaynak tipini değiştirir.
        var status = await (await client.PostJsonAsync("/api/source/start", new StartSourceRequest(null, "nmea"))).ReadAsync<SourceStatusDto>();
        Assert.Equal("Nmea", status.SourceType);
        Assert.Equal("test-alici", status.ActiveScenario);
        Assert.True(status.IsRunning);

        var delta = SeedData.Vehicles.Single(v => v.Callsign == "DELTA-4").Id;
        PositionHistoryDto? history = null;
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(250);
            var from = Uri.EscapeDataString(start.AddMinutes(-1).ToString("O"));
            var to = Uri.EscapeDataString(start.AddMinutes(1).ToString("O"));
            history = await (await client.GetAsync($"/api/vehicles/{delta}/positions?from={from}&to={to}")).ReadAsync<PositionHistoryDto>();
            if (history.Points.Count >= 5) break;
        }

        Assert.NotNull(history);
        Assert.Equal(5, history.Points.Count);
        Assert.All(history.Points, p => Assert.Equal("nmea", p.Source));
        Assert.Equal(39.93, history.Points[0].Latitude, 6);
        Assert.Equal(39.932, history.Points[^1].Latitude, 6);

        var stopped = await (await client.PostAsync("/api/source/stop", null)).ReadAsync<SourceStatusDto>();
        Assert.False(stopped.IsRunning);
        Assert.Equal("Nmea", stopped.SourceType);
        Assert.Null(stopped.LastError);
    }

    [Fact]
    public async Task Unknown_source_type_is_rejected()
    {
        var response = await factory.CreateClient().PostJsonAsync("/api/source/start", new StartSourceRequest(null, "Yok"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Simulator, File, Nmea", await response.Content.ReadAsStringAsync());
    }
}
