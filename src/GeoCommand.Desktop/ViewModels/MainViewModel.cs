using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoCommand.Contracts;
using GeoCommand.Desktop.Services;
using Microsoft.Extensions.Logging;

namespace GeoCommand.Desktop.ViewModels;

/// <summary>Ana ekran. Bölge, görev ve geçmiş işlemleri aynı sınıfın diğer partial dosyalarındadır.</summary>
public sealed partial class MainViewModel : ObservableObject
{
    private readonly ApiClient _api;
    private readonly LiveConnection _live;
    private readonly ILogger<MainViewModel> _logger;
    private readonly Dispatcher _dispatcher;
    private bool _hasSynchronizedOnce;

    public MainViewModel(ApiClient api, LiveConnection live, ILogger<MainViewModel> logger)
    {
        _api = api;
        _live = live;
        _logger = logger;
        _dispatcher = Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;
    }

    public OperationsState State { get; } = new();
    public ObservableCollection<VehicleItemViewModel> Vehicles => State.Vehicles;
    public ObservableCollection<GeoEventDto> Events => State.Events;
    public ObservableCollection<ZoneDto> Zones => State.Zones;

    /// <summary>Harita görünümüne, seçili aracın merkeze alınmasını söyler.</summary>
    public event Action<VehicleItemViewModel>? FocusVehicleRequested;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignMissionCommand), nameof(QueryHistoryCommand))]
    private VehicleItemViewModel? _selectedVehicle;

    [ObservableProperty] private LinkState _linkState = LinkState.Connecting;
    [ObservableProperty] private string _linkMessage = "Bağlanılıyor…";
    [ObservableProperty] private string _lastSyncText = "Henüz eşitlenmedi";
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private string? _infoMessage;
    [ObservableProperty] private string _cursorPositionText = "";

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartSourceCommand), nameof(StopSourceCommand))]
    private SourceStatusDto? _sourceStatus;

    [ObservableProperty] private string? _selectedScenario;

    public string SourceStatusText => SourceStatus switch
    {
        null => "Kaynak: —",
        { IsRunning: true } s => $"Çalışıyor: {s.SourceType} / {s.ActiveScenario}",
        { LastError: not null } s => $"{s.SourceType} hata ile durdu",
        var s => $"{s.SourceType} durdu"
    };

    partial void OnSourceStatusChanged(SourceStatusDto? value) => OnPropertyChanged(nameof(SourceStatusText));

    /// <summary>Yerleşik kaynaklar ve API'nin yüklediği plugin'ler.</summary>
    public ObservableCollection<SourceTypeDto> SourceTypes { get; } = [];

    [ObservableProperty] private SourceTypeDto? _selectedSourceType;

    public bool IsLinkDown => LinkState != LinkState.Connected;

    partial void OnLinkStateChanged(LinkState value) => OnPropertyChanged(nameof(IsLinkDown));

    public void Initialize()
    {
        _live.StateChanged += (state, message) => OnUi(() =>
        {
            LinkState = state;
            LinkMessage = message ?? (state == LinkState.Connected ? "Bağlı" : state.ToString());
        });
        _live.Synchronize += SynchronizeAsync;
        _live.VehicleUpdated += dto => OnUi(() => State.ApplyVehicle(dto));
        _live.EventRaised += dto => OnUi(() => State.AddEvent(dto));
        _live.ZoneCreated += dto => OnUi(() => State.UpsertZone(dto));
        _live.ZoneDeleted += id => OnUi(() => State.RemoveZone(id));
        _live.MissionChanged += dto => OnUi(() => OnMissionChanged(dto));
        _live.SourceStatusChanged += dto => OnUi(() => SourceStatus = dto);
        _live.Start();
    }

    /// <summary>
    /// İlk bağlantıda ve her yeniden bağlanmada çağrılır. Canlı olaylara abone olunduktan sonra REST'ten
    /// anlık görüntü alınır; aradaki çakışmaları <see cref="OperationsState"/> çözer.
    /// </summary>
    private async Task SynchronizeAsync()
    {
        try
        {
            var vehicles = _api.GetVehiclesAsync();
            var events = _api.GetEventsAsync(limit: 200);
            var zones = _api.GetZonesAsync();
            var source = _api.GetSourceStatusAsync();
            var sourceTypes = _api.GetSourceTypesAsync();
            await Task.WhenAll(vehicles, events, zones, source, sourceTypes);

            await _dispatcher.InvokeAsync(async () =>
            {
                var missed = State.ApplySnapshot(vehicles.Result, events.Result, zones.Result);
                SourceStatus = source.Result;
                ApplySourceTypes(sourceTypes.Result);
                LastSyncText = $"Son eşitleme: {DateTime.Now:HH:mm:ss}";

                if (_hasSynchronizedOnce)
                    InfoMessage = $"Yeniden bağlanıldı; durum API'den eşitlendi ({vehicles.Result.Count} araç, bağlantı kopukken {missed} yeni olay).";
                _hasSynchronizedOnce = true;
                ErrorMessage = null;

                if (SelectedVehicle is not null)
                {
                    SelectedVehicle = State.FindVehicle(SelectedVehicle.Id);
                    await LoadMissionsAsync();
                }
            }).Task.Unwrap();

            _logger.LogInformation("Durum eşitlendi. Araç={Vehicles} Olay={Events} Bölge={Zones}",
                vehicles.Result.Count, events.Result.Count, zones.Result.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Durum eşitlenemedi.");
            OnUi(() => ErrorMessage = $"Güncel durum alınamadı: {ex.Message}");
        }
    }

    [RelayCommand]
    private Task ResyncAsync() => SynchronizeAsync();

    partial void OnSelectedVehicleChanged(VehicleItemViewModel? value)
    {
        ClearHistory();
        Missions.Clear();
        if (value is null) return;
        FocusVehicleRequested?.Invoke(value);
        _ = RunAsync(LoadMissionsAsync);
    }

    [RelayCommand]
    private void FocusSelected()
    {
        if (SelectedVehicle is not null) FocusVehicleRequested?.Invoke(SelectedVehicle);
    }

    // ---- Veri kaynağı (yerleşik veya plugin) ----

    private void ApplySourceTypes(IReadOnlyList<SourceTypeDto> types)
    {
        // Operatörün seçimi yeniden eşitlemede korunur; ilk seferde API'de seçili kaynak gösterilir.
        var keep = SelectedSourceType?.Name ?? SourceStatus?.SourceType;
        SourceTypes.Clear();
        foreach (var type in types) SourceTypes.Add(type);
        SelectedSourceType = types.FirstOrDefault(t => string.Equals(t.Name, keep, StringComparison.OrdinalIgnoreCase)) ?? types.FirstOrDefault();
    }

    partial void OnSelectedSourceTypeChanged(SourceTypeDto? value)
    {
        if (value is null || (SelectedScenario is not null && value.AvailableScenarios.Contains(SelectedScenario))) return;
        var running = SourceStatus is not null && string.Equals(SourceStatus.SourceType, value.Name, StringComparison.OrdinalIgnoreCase);
        SelectedScenario = (running ? SourceStatus!.ActiveScenario : null)
                           ?? value.AvailableScenarios.FirstOrDefault(s => s == value.DefaultScenario)
                           ?? value.AvailableScenarios.FirstOrDefault();
    }

    private bool CanStartSource() => SourceStatus is not null;
    private bool CanStopSource() => SourceStatus?.IsRunning == true;

    [RelayCommand(CanExecute = nameof(CanStartSource))]
    private Task StartSourceAsync() => RunAsync(async () =>
    {
        SourceStatus = await _api.StartSourceAsync(SelectedSourceType?.Name, SelectedScenario);
        InfoMessage = $"{SourceStatus.SourceType} kaynağı başlatıldı: {SourceStatus.ActiveScenario}";
    });

    [RelayCommand(CanExecute = nameof(CanStopSource))]
    private Task StopSourceAsync() => RunAsync(async () =>
    {
        SourceStatus = await _api.StopSourceAsync();
        InfoMessage = "Veri kaynağı durduruldu. Araçlar kısa süre sonra çevrimdışı görünecek.";
    });

    [RelayCommand]
    private void DismissMessages()
    {
        ErrorMessage = null;
        InfoMessage = null;
    }

    // ---- Yardımcılar ----

    /// <summary>API çağrılarındaki hataları ekrandaki hata bandına yazar; uygulamayı çökertmez.</summary>
    private async Task RunAsync(Func<Task> action)
    {
        try
        {
            ErrorMessage = null;
            await action();
        }
        catch (ApiException ex)
        {
            _logger.LogWarning("API işlemi başarısız: {Message}", ex.Message);
            ErrorMessage = ex.Message;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Beklenmeyen istemci hatası.");
            ErrorMessage = $"Beklenmeyen hata: {ex.Message}";
        }
    }

    private void OnUi(Action action)
    {
        if (_dispatcher.CheckAccess()) action();
        else _dispatcher.BeginInvoke(action);
    }
}
