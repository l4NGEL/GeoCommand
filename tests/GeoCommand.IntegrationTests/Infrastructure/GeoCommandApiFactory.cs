using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using GeoCommand.Contracts;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace GeoCommand.IntegrationTests.Infrastructure;

/// <summary>
/// Gerçek bir PostGIS konteyneri (Testcontainers) üzerinde API'yi bellekte ayağa kaldırır.
/// Docker çalışmıyorsa testler açık bir hata mesajıyla başarısız olur.
/// </summary>
public class GeoCommandApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly PostgreSqlContainer _db = new PostgreSqlBuilder("postgis/postgis:17-3.5")
        .WithDatabase("geocommand_test")
        .WithUsername("geocommand")
        .WithPassword(Guid.NewGuid().ToString("N"))
        .Build();

    /// <summary>Alt sınıflar ek yapılandırma verebilir (ör. dosya kaynağını seçmek).</summary>
    protected virtual IDictionary<string, string?> ExtraSettings => new Dictionary<string, string?>();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:GeoCommand", _db.GetConnectionString());
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("DataSource:AutoStart", "false");
        builder.UseSetting("Liveness:OfflineAfterSeconds", "3600");
        foreach (var (key, value) in ExtraSettings) builder.UseSetting(key, value);
    }

    public async Task InitializeAsync()
    {
        try
        {
            await _db.StartAsync();
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("PostGIS test konteyneri başlatılamadı. Docker Desktop çalışıyor mu?", ex);
        }
        _ = Server; // host'u (ve migration'ı) şimdi başlat
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _db.DisposeAsync();
    }

    public HubConnection CreateHubConnection() =>
        new HubConnectionBuilder()
            .WithUrl(new Uri(Server.BaseAddress, HubRoutes.Operations), o =>
            {
                o.HttpMessageHandlerFactory = _ => Server.CreateHandler();
                o.Transports = HttpTransportType.LongPolling; // TestServer için
            })
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

    /// <summary>TestServer üzerinden gRPC kanalı (HTTP/2 bellek içinde taşınır).</summary>
    public GrpcChannel CreateGrpcChannel() =>
        GrpcChannel.ForAddress(Server.BaseAddress, new GrpcChannelOptions { HttpHandler = Server.CreateHandler() });

    public T GetService<T>() where T : notnull => Services.GetRequiredService<T>();
}

public static class HttpJson
{
    public static async Task<T> ReadAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"{(int)response.StatusCode} {response.StatusCode}: {body}");
        return JsonSerializer.Deserialize<T>(body, GeoCommandApiFactory.Json)!;
    }

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, GeoCommandApiFactory.Json);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, GeoCommandApiFactory.Json);
}
