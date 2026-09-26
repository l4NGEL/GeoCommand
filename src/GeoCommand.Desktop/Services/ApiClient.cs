using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoCommand.Contracts;

namespace GeoCommand.Desktop.Services;

/// <summary>API'den dönen ProblemDetails'i operatörün okuyabileceği tek bir mesaja çevirir.</summary>
public sealed class ApiException(string message, HttpStatusCode? status = null, Exception? inner = null) : Exception(message, inner)
{
    public HttpStatusCode? Status { get; } = status;
}

public sealed class ApiClient(HttpClient http)
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public Task<List<VehicleDto>> GetVehiclesAsync(CancellationToken ct = default) => GetAsync<List<VehicleDto>>("api/vehicles", ct);

    public Task<List<GeoEventDto>> GetEventsAsync(Guid? vehicleId = null, DateTimeOffset? from = null, DateTimeOffset? to = null,
        int limit = 200, CancellationToken ct = default)
    {
        var query = new List<string> { $"limit={limit}" };
        if (vehicleId is not null) query.Add($"vehicleId={vehicleId}");
        if (from is not null) query.Add($"from={Uri.EscapeDataString(from.Value.ToString("O"))}");
        if (to is not null) query.Add($"to={Uri.EscapeDataString(to.Value.ToString("O"))}");
        return GetAsync<List<GeoEventDto>>("api/events?" + string.Join("&", query), ct);
    }

    public Task<PositionHistoryDto> GetHistoryAsync(Guid vehicleId, DateTimeOffset from, DateTimeOffset to, CancellationToken ct = default) =>
        GetAsync<PositionHistoryDto>(
            $"api/vehicles/{vehicleId}/positions?from={Uri.EscapeDataString(from.ToString("O"))}&to={Uri.EscapeDataString(to.ToString("O"))}", ct);

    public Task<List<ZoneDto>> GetZonesAsync(CancellationToken ct = default) => GetAsync<List<ZoneDto>>("api/zones", ct);

    public Task<ZoneDto> CreateZoneAsync(CreateZoneRequest request, CancellationToken ct = default) =>
        SendAsync<ZoneDto>(HttpMethod.Post, "api/zones", request, ct);

    public Task DeleteZoneAsync(Guid zoneId, CancellationToken ct = default) =>
        SendAsync<object?>(HttpMethod.Delete, $"api/zones/{zoneId}", null, ct);

    public Task<List<MissionDto>> GetMissionsAsync(Guid vehicleId, CancellationToken ct = default) =>
        GetAsync<List<MissionDto>>($"api/vehicles/{vehicleId}/missions", ct);

    public Task<MissionDto> AssignMissionAsync(Guid vehicleId, AssignMissionRequest request, CancellationToken ct = default) =>
        SendAsync<MissionDto>(HttpMethod.Post, $"api/vehicles/{vehicleId}/missions", request, ct);

    public Task<MissionDto> ChangeMissionStatusAsync(Guid missionId, MissionState status, CancellationToken ct = default) =>
        SendAsync<MissionDto>(HttpMethod.Put, $"api/missions/{missionId}/status", new ChangeMissionStatusRequest(status), ct);

    public Task<SourceStatusDto> GetSourceStatusAsync(CancellationToken ct = default) => GetAsync<SourceStatusDto>("api/source", ct);

    public Task<SourceStatusDto> StartSourceAsync(string? scenario, CancellationToken ct = default) =>
        SendAsync<SourceStatusDto>(HttpMethod.Post, "api/source/start", new StartSourceRequest(scenario), ct);

    public Task<SourceStatusDto> StopSourceAsync(CancellationToken ct = default) =>
        SendAsync<SourceStatusDto>(HttpMethod.Post, "api/source/stop", null, ct);

    private Task<T> GetAsync<T>(string url, CancellationToken ct) => SendAsync<T>(HttpMethod.Get, url, null, ct);

    private async Task<T> SendAsync<T>(HttpMethod method, string url, object? body, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(method, url);
        if (body is not null) request.Content = JsonContent.Create(body, body.GetType(), options: Json);

        HttpResponseMessage response;
        try
        {
            response = await http.SendAsync(request, ct);
        }
        catch (HttpRequestException ex)
        {
            throw new ApiException($"API'ye ulaşılamadı ({http.BaseAddress}). Sunucunun çalıştığını kontrol edin.", inner: ex);
        }
        catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
        {
            throw new ApiException("API isteği zaman aşımına uğradı.", inner: ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new ApiException(await ReadProblemAsync(response, ct), response.StatusCode);
            if (response.StatusCode == HttpStatusCode.NoContent || typeof(T) == typeof(object)) return default!;
            return (await response.Content.ReadFromJsonAsync<T>(Json, ct))!;
        }
    }

    internal static async Task<string> ReadProblemAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            var problem = await response.Content.ReadFromJsonAsync<JsonElement>(Json, ct);
            var title = problem.TryGetProperty("title", out var t) ? t.GetString() : null;
            var detail = problem.TryGetProperty("detail", out var d) ? d.GetString() : null;
            if (title is not null || detail is not null)
                return string.Join(" ", new[] { title, detail }.Where(s => !string.IsNullOrWhiteSpace(s)));
        }
        catch (JsonException)
        {
        }
        return $"API isteği başarısız oldu ({(int)response.StatusCode} {response.ReasonPhrase}).";
    }
}
