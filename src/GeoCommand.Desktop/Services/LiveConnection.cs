using System.Text.Json.Serialization;
using GeoCommand.Contracts;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace GeoCommand.Desktop.Services;

public enum LinkState
{
    Connecting,
    Connected,
    Reconnecting,
    Disconnected
}

/// <summary>
/// SignalR bağlantısını yönetir. Bağlantı koptuğunda sonsuza kadar artan aralıklarla yeniden dener ve her
/// (yeniden) bağlanmada <see cref="Synchronize"/> olayını tetikler; istemci bu noktada güncel durumu REST'ten çeker.
/// Olaylar arka plan iş parçacığından gelir; UI'a aktarma görevi dinleyicidedir.
/// </summary>
public sealed class LiveConnection : IAsyncDisposable
{
    private static readonly TimeSpan[] Delays = [TimeSpan.Zero, TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)];

    private readonly HubConnection _hub;
    private readonly ILogger<LiveConnection> _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _startLoop;

    public event Action<LinkState, string?>? StateChanged;
    /// <summary>İlk bağlantıda ve her yeniden bağlanmada tetiklenir.</summary>
    public event Func<Task>? Synchronize;
    public event Action<VehicleDto>? VehicleUpdated;
    public event Action<GeoEventDto>? EventRaised;
    public event Action<MissionDto>? MissionChanged;
    public event Action<ZoneDto>? ZoneCreated;
    public event Action<Guid>? ZoneDeleted;
    public event Action<SourceStatusDto>? SourceStatusChanged;

    public LinkState State { get; private set; } = LinkState.Disconnected;

    public LiveConnection(Uri apiBaseUrl, ILogger<LiveConnection> logger)
    {
        _logger = logger;
        _hub = new HubConnectionBuilder()
            .WithUrl(new Uri(apiBaseUrl, HubRoutes.Operations))
            .WithAutomaticReconnect(new ForeverRetryPolicy())
            .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()))
            .Build();

        _hub.On<VehicleDto>(nameof(IOperationsClient.VehicleUpdated), v => VehicleUpdated?.Invoke(v));
        _hub.On<GeoEventDto>(nameof(IOperationsClient.EventRaised), e => EventRaised?.Invoke(e));
        _hub.On<MissionDto>(nameof(IOperationsClient.MissionChanged), m => MissionChanged?.Invoke(m));
        _hub.On<ZoneDto>(nameof(IOperationsClient.ZoneCreated), z => ZoneCreated?.Invoke(z));
        _hub.On<Guid>(nameof(IOperationsClient.ZoneDeleted), id => ZoneDeleted?.Invoke(id));
        _hub.On<SourceStatusDto>(nameof(IOperationsClient.SourceStatusChanged), s => SourceStatusChanged?.Invoke(s));

        _hub.Reconnecting += ex =>
        {
            _logger.LogWarning("SignalR bağlantısı koptu, yeniden bağlanılıyor: {Reason}", ex?.Message);
            SetState(LinkState.Reconnecting, "Bağlantı koptu; yeniden bağlanılıyor…");
            return Task.CompletedTask;
        };
        _hub.Reconnected += async _ =>
        {
            _logger.LogInformation("SignalR yeniden bağlandı.");
            SetState(LinkState.Connected, null);
            await RaiseSynchronizeAsync();
        };
        _hub.Closed += ex =>
        {
            // Otomatik yeniden bağlanma vazgeçerse (veya sunucu kapatırsa) baştan başlat.
            if (_cts.IsCancellationRequested) return Task.CompletedTask;
            _logger.LogWarning("SignalR bağlantısı kapandı: {Reason}", ex?.Message);
            SetState(LinkState.Disconnected, "Bağlantı kapandı; yeniden deneniyor…");
            _startLoop = Task.Run(() => StartLoopAsync(_cts.Token));
            return Task.CompletedTask;
        };
    }

    public void Start() => _startLoop ??= Task.Run(() => StartLoopAsync(_cts.Token));

    private async Task StartLoopAsync(CancellationToken ct)
    {
        var attempt = 0;
        while (!ct.IsCancellationRequested)
        {
            attempt++;
            SetState(LinkState.Connecting, attempt == 1 ? "Bağlanılıyor…" : $"Sunucuya ulaşılamıyor; tekrar deneniyor (deneme {attempt})…");
            try
            {
                await _hub.StartAsync(ct);
                _logger.LogInformation("SignalR bağlandı. Deneme={Attempt}", attempt);
                SetState(LinkState.Connected, null);
                await RaiseSynchronizeAsync();
                return;
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("SignalR bağlantısı kurulamadı (deneme {Attempt}): {Reason}", attempt, ex.Message);
                SetState(LinkState.Disconnected, $"Sunucuya ulaşılamıyor: {ex.Message}");
                await Task.Delay(Delays[Math.Min(attempt, Delays.Length - 1)], ct).ContinueWith(_ => { }, CancellationToken.None);
            }
        }
    }

    private async Task RaiseSynchronizeAsync()
    {
        if (Synchronize is null) return;
        try
        {
            await Synchronize.Invoke();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Yeniden bağlanma sonrası eşitleme başarısız oldu.");
        }
    }

    private void SetState(LinkState state, string? message)
    {
        State = state;
        StateChanged?.Invoke(state, message);
    }

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        await _hub.DisposeAsync();
    }

    private sealed class ForeverRetryPolicy : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) =>
            Delays[Math.Min((int)retryContext.PreviousRetryCount, Delays.Length - 1)];
    }
}
