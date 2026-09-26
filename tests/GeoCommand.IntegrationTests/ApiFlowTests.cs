using System.Net;
using GeoCommand.Contracts;
using GeoCommand.IntegrationTests.Infrastructure;
using GeoCommand.Infrastructure.Persistence;

namespace GeoCommand.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<GeoCommandApiFactory>
{
    public const string Name = "api";
}

/// <summary>
/// Kritik akış: HTTP bildirim → doğrulama → PostGIS'e kayıt → bölge olayı → SignalR yayını → REST sorgusu.
/// Her test farklı bir araç ve farklı bir coğrafi alan kullanır; böylece birbirini etkilemez.
/// </summary>
[Collection(ApiCollection.Name)]
public sealed class ApiFlowTests(GeoCommandApiFactory factory)
{
    private readonly HttpClient _client = factory.CreateClient();

    // Karşılaştırmalar veritabanı hassasiyetinden (mikrosaniye) etkilenmesin diye tam saniye.
    private static DateTimeOffset WholeSeconds(DateTimeOffset d) => new(d.Ticks - d.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);

    private static Guid IdOf(string callsign) => SeedData.Vehicles.Single(v => v.Callsign == callsign).Id;

    private Task<HttpResponseMessage> ReportAsync(string callsign, double lat, double lon, DateTimeOffset at, double speed = 10) =>
        _client.PostJsonAsync("/api/positions", new PositionReportRequest(callsign, lat, lon, speed, 90, at));

    private async Task<ZoneDto> CreateSquareZoneAsync(string name, double south, double west, double size)
    {
        var response = await _client.PostJsonAsync("/api/zones", new CreateZoneRequest(name,
        [
            new(south, west), new(south, west + size), new(south + size, west + size), new(south + size, west)
        ]));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsync<ZoneDto>();
    }

    [Fact]
    public async Task Crossing_a_zone_raises_exactly_one_enter_and_one_exit_event_and_broadcasts_them()
    {
        await using var live = await SignalRRecorder.ConnectAsync(factory);
        var zone = await CreateSquareZoneAsync("Test Bölgesi A", south: 40.50, west: 33.50, size: 0.01);
        var t0 = WholeSeconds(DateTimeOffset.UtcNow.AddMinutes(-1));

        Assert.Equal(HttpStatusCode.Accepted, (await ReportAsync("ALFA-1", 40.495, 33.505, t0)).StatusCode);              // dışarıda
        Assert.Equal(HttpStatusCode.Accepted, (await ReportAsync("ALFA-1", 40.505, 33.505, t0.AddSeconds(1))).StatusCode); // girdi
        Assert.Equal(HttpStatusCode.Accepted, (await ReportAsync("ALFA-1", 40.506, 33.506, t0.AddSeconds(2))).StatusCode); // içeride kaldı

        // Aynı bildirim tekrar gelirse (ör. kaynak yeniden bağlanıp tekrar gönderdi) reddedilir ve olay üretmez.
        var duplicate = await ReportAsync("ALFA-1", 40.506, 33.506, t0.AddSeconds(2));
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("Duplicate", await duplicate.Content.ReadAsStringAsync());

        // PostGIS tarafında ST_Covers ile bölgedeki araçlar
        var inside = await (await _client.GetAsync($"/api/zones/{zone.Id}/vehicles")).ReadAsync<List<VehicleDto>>();
        Assert.Equal(["ALFA-1"], inside.Select(v => v.Callsign));

        Assert.Equal(HttpStatusCode.Accepted, (await ReportAsync("ALFA-1", 40.515, 33.505, t0.AddSeconds(3))).StatusCode); // çıktı

        var events = await (await _client.GetAsync($"/api/events?vehicleId={IdOf("ALFA-1")}")).ReadAsync<List<GeoEventDto>>();
        var zoneEvents = events.Where(e => e.ZoneId == zone.Id).OrderBy(e => e.OccurredAtUtc).ToList();
        Assert.Equal([GeoEventType.ZoneEntered, GeoEventType.ZoneExited], zoneEvents.Select(e => e.Type));
        Assert.Equal(t0.AddSeconds(1), zoneEvents[0].OccurredAtUtc);
        Assert.Contains("Test Bölgesi A", zoneEvents[0].Message);

        // Aynı olaylar SignalR ile canlı olarak da geldi.
        await SignalRRecorder.WaitForAsync(live.Events, e => e.ZoneId == zone.Id && e.Type == GeoEventType.ZoneEntered);
        await SignalRRecorder.WaitForAsync(live.Events, e => e.ZoneId == zone.Id && e.Type == GeoEventType.ZoneExited);
        await SignalRRecorder.WaitForAsync(live.Vehicles, v => v.Callsign == "ALFA-1" && v.Latitude == 40.515);
        await SignalRRecorder.WaitForAsync(live.Zones, z => z.Id == zone.Id);
        Assert.Equal(2, live.Events.Count(e => e.ZoneId == zone.Id));
    }

    [Fact]
    public async Task Mission_assignment_and_position_history_work_end_to_end()
    {
        await using var live = await SignalRRecorder.ConnectAsync(factory);
        var vehicleId = IdOf("BRAVO-2");
        var t0 = WholeSeconds(DateTimeOffset.UtcNow.AddMinutes(-10));
        for (var i = 0; i < 6; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await ReportAsync("BRAVO-2", 41.0 + i * 0.001, 34.0, t0.AddSeconds(i * 10))).StatusCode);

        // Görev ata
        var assign = await _client.PostJsonAsync($"/api/vehicles/{vehicleId}/missions",
            new AssignMissionRequest("Kuzey kontrol noktasına git", MissionPriority.High));
        Assert.Equal(HttpStatusCode.Created, assign.StatusCode);
        var mission = await assign.ReadAsync<MissionDto>();
        Assert.Equal(MissionState.Assigned, mission.Status);
        Assert.Equal(MissionPriority.High, mission.Priority);
        Assert.InRange(mission.AssignedAtUtc, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));

        var vehicle = await (await _client.GetAsync($"/api/vehicles/{vehicleId}")).ReadAsync<VehicleDto>();
        Assert.Equal(VehicleState.OnMission, vehicle.Status);
        Assert.Equal(mission.Id, vehicle.ActiveMissionId);

        // İkinci aktif görev reddedilir
        var second = await _client.PostJsonAsync($"/api/vehicles/{vehicleId}/missions", new AssignMissionRequest("İkinci", MissionPriority.Low));
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

        // Durum geçişleri
        Assert.Equal(HttpStatusCode.Conflict,
            (await _client.PutJsonAsync($"/api/missions/{mission.Id}/status", new ChangeMissionStatusRequest(MissionState.Completed))).StatusCode);
        await (await _client.PutJsonAsync($"/api/missions/{mission.Id}/status", new ChangeMissionStatusRequest(MissionState.InProgress))).ReadAsync<MissionDto>();
        var done = await (await _client.PutJsonAsync($"/api/missions/{mission.Id}/status", new ChangeMissionStatusRequest(MissionState.Completed))).ReadAsync<MissionDto>();
        Assert.Equal(MissionState.Completed, done.Status);

        var missions = await (await _client.GetAsync($"/api/vehicles/{vehicleId}/missions")).ReadAsync<List<MissionDto>>();
        Assert.Single(missions);
        await SignalRRecorder.WaitForAsync(live.Missions, m => m.Id == mission.Id && m.Status == MissionState.Completed);

        // Tarih aralığına göre konum geçmişi
        string Iso(DateTimeOffset d) => Uri.EscapeDataString(d.ToString("O"));
        var all = await (await _client.GetAsync($"/api/vehicles/{vehicleId}/positions?from={Iso(t0)}&to={Iso(t0.AddMinutes(5))}"))
            .ReadAsync<PositionHistoryDto>();
        Assert.Equal(6, all.Points.Count);
        Assert.Equal(all.Points.OrderBy(p => p.RecordedAtUtc), all.Points);
        Assert.InRange(all.DistanceMeters, 5 * 111 - 5, 5 * 111 + 5); // 5 x 0.001° enlem ≈ 555 m
        Assert.All(all.Points, p => Assert.Equal("http", p.Source));

        var window = await (await _client.GetAsync($"/api/vehicles/{vehicleId}/positions?from={Iso(t0.AddSeconds(15))}&to={Iso(t0.AddSeconds(35))}"))
            .ReadAsync<PositionHistoryDto>();
        Assert.Equal([t0.AddSeconds(20), t0.AddSeconds(30)], window.Points.Select(p => p.RecordedAtUtc));

        var limited = await (await _client.GetAsync($"/api/vehicles/{vehicleId}/positions?from={Iso(t0)}&to={Iso(t0.AddMinutes(5))}&limit=4"))
            .ReadAsync<PositionHistoryDto>();
        Assert.True(limited.Truncated);
        Assert.Equal(4, limited.Points.Count);

        // Olay geçmişi: görev olayları araç filtresiyle gelir
        var events = await (await _client.GetAsync($"/api/events?vehicleId={vehicleId}")).ReadAsync<List<GeoEventDto>>();
        Assert.Contains(events, e => e.Type == GeoEventType.MissionAssigned && e.MissionId == mission.Id);
        Assert.Equal(2, events.Count(e => e.Type == GeoEventType.MissionStatusChanged));
    }

    [Fact]
    public async Task Invalid_input_returns_readable_problem_details()
    {
        var badZone = await _client.PostJsonAsync("/api/zones", new CreateZoneRequest("", [new(95, 32), new(39, 32)]));
        Assert.Equal(HttpStatusCode.BadRequest, badZone.StatusCode);
        var body = await badZone.Content.ReadAsStringAsync();
        Assert.Contains("Bölge adı", body);
        Assert.Contains("\"errors\"", body);

        var bowTie = await _client.PostJsonAsync("/api/zones", new CreateZoneRequest("Papyon",
            [new(39.0, 32.0), new(39.1, 32.1), new(39.0, 32.1), new(39.1, 32.0)]));
        Assert.Contains("kendini kesiyor", await bowTie.Content.ReadAsStringAsync());

        var unknown = await ReportAsync("ZULU-9", 39.9, 32.8, DateTimeOffset.UtcNow);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);

        var invalid = await ReportAsync("CHARLIE-3", 99, 32.8, DateTimeOffset.UtcNow.AddHours(1), speed: -1);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        var invalidBody = await invalid.Content.ReadAsStringAsync();
        Assert.Contains("Enlem", invalidBody);
        Assert.Contains("gelecekte", invalidBody);

        var reversed = await _client.GetAsync($"/api/vehicles/{IdOf("CHARLIE-3")}/positions?from=2026-02-01T00:00:00Z&to=2026-01-01T00:00:00Z");
        Assert.Equal(HttpStatusCode.BadRequest, reversed.StatusCode);

        var missingVehicle = await _client.GetAsync($"/api/vehicles/{Guid.NewGuid()}/missions");
        Assert.Equal(HttpStatusCode.NotFound, missingVehicle.StatusCode);

        var badEnum = await _client.PostAsync($"/api/vehicles/{IdOf("CHARLIE-3")}/missions",
            new StringContent("""{"description":"x","priority":"Mega"}""", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, badEnum.StatusCode);
        Assert.Contains("İstek okunamadı", await badEnum.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Zone_created_around_vehicle_does_not_emit_enter_event_but_later_exit_does()
    {
        var t0 = WholeSeconds(DateTimeOffset.UtcNow.AddMinutes(-5));
        await ReportAsync("DELTA-4", 42.005, 35.005, t0);

        var zone = await CreateSquareZoneAsync("Mevcut konum", south: 42.0, west: 35.0, size: 0.01);
        await ReportAsync("DELTA-4", 42.006, 35.006, t0.AddSeconds(1)); // hâlâ içeride
        await ReportAsync("DELTA-4", 42.020, 35.006, t0.AddSeconds(2)); // çıktı

        var events = await (await _client.GetAsync($"/api/events?vehicleId={IdOf("DELTA-4")}")).ReadAsync<List<GeoEventDto>>();
        Assert.Equal([GeoEventType.ZoneExited], events.Where(e => e.ZoneId == zone.Id).Select(e => e.Type));

        Assert.Equal(HttpStatusCode.NoContent, (await _client.DeleteAsync($"/api/zones/{zone.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await _client.DeleteAsync($"/api/zones/{zone.Id}")).StatusCode);
    }
}
