using GeoCommand.Infrastructure;
using GeoCommand.Infrastructure.DataSources;
using GeoCommand.Infrastructure.DataSources.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace GeoCommand.UnitTests.Plugins;

/// <summary>API'nin kullandığı DI kaydı üzerinden: plugin klasörü çözümü ve DataSource:Type doğrulaması.</summary>
public class DataSourceSelectionTests
{
    private static ServiceProvider Build(string contentRoot, params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.Select(s => new KeyValuePair<string, string?>(s.Key, s.Value)))
            .Build();
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(configuration)
            .AddSingleton<IHostEnvironment>(new FakeEnvironment(contentRoot))
            .AddSingleton(TimeProvider.System)
            .AddLogging()
            .AddInfrastructure(configuration)
            .BuildServiceProvider();
    }

    [Fact]
    public void Relative_plugin_directory_is_resolved_against_the_content_root()
    {
        // API projesi klasörü content root'tur; Development ayarı "../../artifacts/plugins" bu yüzden çalışır.
        using var services = Build(Path.Combine(RepoPaths.Root, "src", "GeoCommand.Api"),
            ("DataSource:Plugins:Directory", "../../artifacts/plugins"),
            ("DataSource:Type", "Nmea"));

        var catalog = services.GetRequiredService<PositionSourceCatalog>();

        Assert.Equal(["Simulator", "File", "Nmea"], catalog.Entries.Select(e => e.Name));
        Assert.Equal("Nmea", services.GetRequiredService<IOptions<DataSourceOptions>>().Value.Type);
    }

    [Fact]
    public void Unknown_source_type_fails_validation_and_lists_what_is_available()
    {
        using var services = Build(RepoPaths.Root, ("DataSource:Plugins:Directory", ""), ("DataSource:Type", "Nmea"));

        var ex = Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<DataSourceOptions>>().Value);

        Assert.Contains("'Nmea' tanınmıyor", ex.Message);
        Assert.Contains("Simulator, File", ex.Message);
    }

    private sealed class FakeEnvironment(string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "GeoCommand.UnitTests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
