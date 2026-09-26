using GeoCommand.Contracts;
using GeoCommand.IntegrationTests.Infrastructure;
using GeoCommand.Infrastructure.Persistence;

namespace GeoCommand.IntegrationTests;

/// <summary>Yalnızca yapılandırma değiştirilerek simülatör yerine CSV kayıt dosyası kaynağı kullanılır.</summary>
public sealed class FileSourceFactory : GeoCommandApiFactory
{
    protected override IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>
    {
        ["DataSource:Type"] = "File",
        ["DataSource:AutoStart"] = "true",
        ["DataSource:File:PlaybackSpeed"] = "50",
        ["DataSource:File:Loop"] = "false"
    };
}

public sealed class FileSourceTests(FileSourceFactory factory) : IClassFixture<FileSourceFactory>
{
    [Fact]
    public async Task File_source_selected_by_configuration_feeds_the_same_ingestion_pipeline()
    {
        var client = factory.CreateClient();

        var status = await (await client.GetAsync("/api/source")).ReadAsync<SourceStatusDto>();
        Assert.Equal("File", status.SourceType);
        Assert.Contains("ankara-kayit.csv", status.AvailableScenarios);

        // 300 sn'lik kayıt 50x hızda ~6 sn sürer; ilk birkaç saniyede konumlar gelmeye başlamalı.
        var alfa = SeedData.Vehicles.Single(v => v.Callsign == "ALFA-1").Id;
        PositionHistoryDto? history = null;
        for (var i = 0; i < 40; i++)
        {
            await Task.Delay(250);
            var from = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(-5).ToString("O"));
            var to = Uri.EscapeDataString(DateTimeOffset.UtcNow.AddMinutes(5).ToString("O"));
            history = await (await client.GetAsync($"/api/vehicles/{alfa}/positions?from={from}&to={to}")).ReadAsync<PositionHistoryDto>();
            if (history.Points.Count >= 5) break;
        }

        Assert.NotNull(history);
        Assert.True(history.Points.Count >= 5, $"Dosya kaynağından yeterli konum gelmedi ({history.Points.Count}).");
        Assert.All(history.Points, p => Assert.Equal("file", p.Source));
        Assert.Equal(39.9060000, history.Points[0].Latitude, 6); // kayıt dosyasının ilk ALFA-1 satırı

        var vehicles = await (await client.GetAsync("/api/vehicles")).ReadAsync<List<VehicleDto>>();
        Assert.Contains(vehicles, v => v.Callsign == "BRAVO-2" && v.LastUpdateUtc is not null);
        Assert.Contains(vehicles, v => v.Callsign == "CHARLIE-3" && v.LastUpdateUtc is not null);
    }

    [Fact]
    public async Task Unknown_scenario_is_rejected_without_touching_the_file_system()
    {
        var client = factory.CreateClient();

        var response = await client.PostJsonAsync("/api/source/start", new StartSourceRequest("..\\..\\appsettings.json"));

        Assert.Equal(System.Net.HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("bulunamadı", await response.Content.ReadAsStringAsync());
    }
}
