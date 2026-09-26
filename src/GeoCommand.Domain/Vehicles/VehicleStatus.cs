namespace GeoCommand.Domain.Vehicles;

public enum VehicleStatus
{
    /// <summary>Henüz konum bildirimi alınmadı.</summary>
    Unknown = 0,
    Idle = 1,
    Moving = 2,
    OnMission = 3,
    /// <summary>Belirlenen süre boyunca konum bildirimi gelmedi.</summary>
    Offline = 4
}
