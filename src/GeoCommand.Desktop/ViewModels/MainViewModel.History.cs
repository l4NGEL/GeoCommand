using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoCommand.Contracts;

namespace GeoCommand.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public const string TimeFormat = "dd.MM.yyyy HH:mm";
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");

    public ObservableCollection<PositionDto> HistoryPoints { get; } = [];
    public ObservableCollection<GeoEventDto> HistoryEvents { get; } = [];

    [ObservableProperty] private string _historyFrom = DateTime.Now.AddMinutes(-15).ToString(TimeFormat, Tr);
    [ObservableProperty] private string _historyTo = DateTime.Now.AddMinutes(1).ToString(TimeFormat, Tr);
    [ObservableProperty] private string? _historySummary;
    [ObservableProperty] private bool _showHistoryOnMap = true;

    /// <summary>Harita görünümü izi yeniden çizmek için dinler.</summary>
    public event Action? HistoryChanged;

    partial void OnShowHistoryOnMapChanged(bool value) => HistoryChanged?.Invoke();

    [RelayCommand]
    private void QuickRange(string? minutes)
    {
        if (!int.TryParse(minutes, out var m)) return;
        HistoryFrom = DateTime.Now.AddMinutes(-m).ToString(TimeFormat, Tr);
        HistoryTo = DateTime.Now.AddMinutes(1).ToString(TimeFormat, Tr);
        if (QueryHistoryCommand.CanExecute(null)) QueryHistoryCommand.Execute(null);
    }

    private bool CanQueryHistory() => SelectedVehicle is not null;

    [RelayCommand(CanExecute = nameof(CanQueryHistory))]
    private Task QueryHistoryAsync() => RunAsync(async () =>
    {
        if (!TryParseLocal(HistoryFrom, out var from) || !TryParseLocal(HistoryTo, out var to))
        {
            ErrorMessage = $"Tarih biçimi '{TimeFormat}' olmalı (ör. {DateTime.Now.ToString(TimeFormat, Tr)}).";
            return;
        }

        var vehicle = SelectedVehicle!;
        var history = _api.GetHistoryAsync(vehicle.Id, from, to);
        var events = _api.GetEventsAsync(vehicle.Id, from, to, limit: 1000);
        await Task.WhenAll(history, events);

        HistoryPoints.Clear();
        foreach (var p in history.Result.Points) HistoryPoints.Add(p);
        HistoryEvents.Clear();
        foreach (var e in events.Result) HistoryEvents.Add(e);

        HistorySummary = $"{vehicle.Callsign}: {history.Result.Points.Count} konum, {history.Result.DistanceMeters / 1000:0.00} km, {events.Result.Count} olay"
                         + (history.Result.Truncated ? " — sonuç sınıra ulaştı, aralığı daraltın." : "");
        HistoryChanged?.Invoke();
    });

    [RelayCommand]
    private void ClearHistory()
    {
        HistoryPoints.Clear();
        HistoryEvents.Clear();
        HistorySummary = null;
        HistoryChanged?.Invoke();
    }

    internal static bool TryParseLocal(string text, out DateTimeOffset value)
    {
        if (DateTime.TryParseExact(text?.Trim(), TimeFormat, Tr, DateTimeStyles.AssumeLocal, out var local))
        {
            value = new DateTimeOffset(local);
            return true;
        }
        value = default;
        return false;
    }
}
