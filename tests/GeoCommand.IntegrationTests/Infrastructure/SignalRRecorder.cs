using System.Collections.Concurrent;
using GeoCommand.Contracts;
using Microsoft.AspNetCore.SignalR.Client;

namespace GeoCommand.IntegrationTests.Infrastructure;

/// <summary>SignalR yayınlarını toplar ve bekleme yardımcıları sağlar.</summary>
public sealed class SignalRRecorder : IAsyncDisposable
{
    private readonly HubConnection _connection;

    public ConcurrentQueue<VehicleDto> Vehicles { get; } = new();
    public ConcurrentQueue<GeoEventDto> Events { get; } = new();
    public ConcurrentQueue<MissionDto> Missions { get; } = new();
    public ConcurrentQueue<ZoneDto> Zones { get; } = new();

    private SignalRRecorder(HubConnection connection)
    {
        _connection = connection;
        connection.On<VehicleDto>(nameof(IOperationsClient.VehicleUpdated), Vehicles.Enqueue);
        connection.On<GeoEventDto>(nameof(IOperationsClient.EventRaised), Events.Enqueue);
        connection.On<MissionDto>(nameof(IOperationsClient.MissionChanged), Missions.Enqueue);
        connection.On<ZoneDto>(nameof(IOperationsClient.ZoneCreated), Zones.Enqueue);
    }

    public static async Task<SignalRRecorder> ConnectAsync(GeoCommandApiFactory factory)
    {
        var recorder = new SignalRRecorder(factory.CreateHubConnection());
        await recorder._connection.StartAsync();
        return recorder;
    }

    public static async Task<T> WaitForAsync<T>(ConcurrentQueue<T> queue, Func<T, bool> predicate, TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(10));
        while (DateTime.UtcNow < deadline)
        {
            var match = queue.FirstOrDefault(predicate);
            if (match is not null) return match;
            await Task.Delay(50);
        }
        throw new TimeoutException($"Beklenen SignalR mesajı gelmedi ({typeof(T).Name}).");
    }

    public async ValueTask DisposeAsync() => await _connection.DisposeAsync();
}
