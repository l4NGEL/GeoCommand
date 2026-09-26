using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GeoCommand.Contracts;

namespace GeoCommand.Desktop.ViewModels;

public sealed partial class MainViewModel
{
    public ObservableCollection<MissionItemViewModel> Missions { get; } = [];

    public IReadOnlyList<MissionPriority> Priorities { get; } = Enum.GetValues<MissionPriority>();

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(AssignMissionCommand))]
    private string _missionDescription = "";

    [ObservableProperty] private MissionPriority _missionPriority = MissionPriority.Normal;

    private async Task LoadMissionsAsync()
    {
        var vehicle = SelectedVehicle;
        if (vehicle is null) return;
        var missions = await _api.GetMissionsAsync(vehicle.Id);
        if (SelectedVehicle?.Id != vehicle.Id) return; // bu arada seçim değişti
        Missions.Clear();
        foreach (var m in missions) Missions.Add(new MissionItemViewModel(m));
    }

    private bool CanAssignMission() => SelectedVehicle is not null && !string.IsNullOrWhiteSpace(MissionDescription);

    [RelayCommand(CanExecute = nameof(CanAssignMission))]
    private Task AssignMissionAsync() => RunAsync(async () =>
    {
        var vehicle = SelectedVehicle!;
        var mission = await _api.AssignMissionAsync(vehicle.Id, new AssignMissionRequest(MissionDescription.Trim(), MissionPriority));
        OnMissionChanged(mission);
        MissionDescription = "";
        InfoMessage = $"{vehicle.Callsign} aracına görev atandı (simülasyon içi).";
    });

    [RelayCommand]
    private Task StartMissionAsync(MissionItemViewModel? item) => ChangeMissionAsync(item, MissionState.InProgress);

    [RelayCommand]
    private Task CompleteMissionAsync(MissionItemViewModel? item) => ChangeMissionAsync(item, MissionState.Completed);

    [RelayCommand]
    private Task CancelMissionAsync(MissionItemViewModel? item) => ChangeMissionAsync(item, MissionState.Cancelled);

    private Task ChangeMissionAsync(MissionItemViewModel? item, MissionState target) => item is null ? Task.CompletedTask : RunAsync(async () =>
    {
        var updated = await _api.ChangeMissionStatusAsync(item.Mission.Id, target);
        OnMissionChanged(updated);
    });

    /// <summary>Hem kendi isteğimizin yanıtı hem SignalR yayını buraya düşer; kimliğe göre güncellenir.</summary>
    private void OnMissionChanged(MissionDto mission)
    {
        if (SelectedVehicle?.Id != mission.VehicleId) return;
        var index = Missions.ToList().FindIndex(m => m.Mission.Id == mission.Id);
        if (index >= 0)
        {
            if (Missions[index].Mission.UpdatedAtUtc <= mission.UpdatedAtUtc) Missions[index] = new MissionItemViewModel(mission);
        }
        else
        {
            Missions.Insert(0, new MissionItemViewModel(mission));
        }
    }
}
