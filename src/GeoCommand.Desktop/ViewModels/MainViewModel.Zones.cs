using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoCommand.Contracts;

namespace GeoCommand.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    /// <summary>Çizim sırasında haritaya tıklanarak eklenen köşeler (WGS84).</summary>
    public ObservableCollection<GeoPointDto> DraftVertices { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveZoneCommand), nameof(CancelZoneCommand), nameof(UndoVertexCommand), nameof(BeginZoneCommand))]
    private bool _isDrawingZone;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SaveZoneCommand))]
    private string _newZoneName = "";

    /// <summary>Harita görünümü tıklanan noktayı buraya iletir (enlem/boylam; ekran koordinatı değil).</summary>
    public void AddDraftVertex(double latitude, double longitude)
    {
        if (!IsDrawingZone) return;
        DraftVertices.Add(new GeoPointDto(Math.Round(latitude, 6), Math.Round(longitude, 6)));
        SaveZoneCommand.NotifyCanExecuteChanged();
        UndoVertexCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanBeginZone))]
    private void BeginZone()
    {
        DraftVertices.Clear();
        NewZoneName = $"Bölge {Zones.Count + 1}";
        IsDrawingZone = true;
        InfoMessage = "Haritaya tıklayarak en az 3 köşe ekleyin, ardından 'Kaydet'e basın.";
    }

    private bool CanBeginZone() => !IsDrawingZone;

    [RelayCommand(CanExecute = nameof(CanUndoVertex))]
    private void UndoVertex()
    {
        if (DraftVertices.Count > 0) DraftVertices.RemoveAt(DraftVertices.Count - 1);
        SaveZoneCommand.NotifyCanExecuteChanged();
        UndoVertexCommand.NotifyCanExecuteChanged();
    }

    private bool CanUndoVertex() => IsDrawingZone && DraftVertices.Count > 0;

    [RelayCommand(CanExecute = nameof(CanSaveZone))]
    private Task SaveZoneAsync() => RunAsync(async () =>
    {
        var zone = await _api.CreateZoneAsync(new CreateZoneRequest(NewZoneName, DraftVertices.ToList()));
        State.UpsertZone(zone); // SignalR ile de gelir; Upsert yinelenmeyi önler
        IsDrawingZone = false;
        DraftVertices.Clear();
        InfoMessage = $"'{zone.Name}' bölgesi oluşturuldu. Araçlar girip çıktıkça olay listesine düşecek.";
    });

    private bool CanSaveZone() => IsDrawingZone && DraftVertices.Count >= 3 && !string.IsNullOrWhiteSpace(NewZoneName);

    [RelayCommand(CanExecute = nameof(IsDrawingZone))]
    private void CancelZone()
    {
        IsDrawingZone = false;
        DraftVertices.Clear();
        InfoMessage = null;
    }

    [RelayCommand]
    private Task DeleteZoneAsync(ZoneDto? zone) => zone is null ? Task.CompletedTask : RunAsync(async () =>
    {
        await _api.DeleteZoneAsync(zone.Id);
        State.RemoveZone(zone.Id);
        InfoMessage = $"'{zone.Name}' bölgesi silindi. Geçmiş olaylar korunur.";
    });
}
