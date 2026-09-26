using GeoCommand.Contracts;

namespace GeoCommand.Desktop.ViewModels;

public sealed record MissionItemViewModel(MissionDto Mission)
{
    public bool CanStart => Mission.Status == MissionState.Assigned;
    public bool CanComplete => Mission.Status == MissionState.InProgress;
    public bool CanCancel => Mission.Status is MissionState.Assigned or MissionState.InProgress;
}
