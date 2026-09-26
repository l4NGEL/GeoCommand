using CommunityToolkit.Mvvm.ComponentModel;
using GeoCommand.Contracts;

namespace GeoCommand.Desktop.ViewModels;

public sealed partial class VehicleItemViewModel(VehicleDto dto) : ObservableObject
{
    public Guid Id { get; } = dto.Id;
    public string Callsign { get; } = dto.Callsign;

    [ObservableProperty] private VehicleState _status = dto.Status;
    [ObservableProperty] private double? _latitude = dto.Latitude;
    [ObservableProperty] private double? _longitude = dto.Longitude;
    [ObservableProperty] private double _speedMps = dto.SpeedMps;
    [ObservableProperty] private double _headingDegrees = dto.HeadingDegrees;
    [ObservableProperty] private DateTimeOffset? _lastUpdateUtc = dto.LastUpdateUtc;
    [ObservableProperty] private Guid? _activeMissionId = dto.ActiveMissionId;

    public double SpeedKmh => SpeedMps * 3.6;
    public bool HasPosition => Latitude is not null && Longitude is not null;

    partial void OnSpeedMpsChanged(double value) => OnPropertyChanged(nameof(SpeedKmh));
    partial void OnLatitudeChanged(double? value) => OnPropertyChanged(nameof(HasPosition));

    public void Apply(VehicleDto dto)
    {
        Status = dto.Status;
        Latitude = dto.Latitude;
        Longitude = dto.Longitude;
        SpeedMps = dto.SpeedMps;
        HeadingDegrees = dto.HeadingDegrees;
        LastUpdateUtc = dto.LastUpdateUtc;
        ActiveMissionId = dto.ActiveMissionId;
    }
}
