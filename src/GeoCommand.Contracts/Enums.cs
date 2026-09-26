namespace GeoCommand.Contracts;

// Domain enum'larıyla aynı değerleri taşır; istemci Domain'e bağımlı olmadan aynı sözlüğü konuşur.
// JSON'da metin olarak serileştirilir.

public enum VehicleState
{
    Unknown = 0,
    Idle = 1,
    Moving = 2,
    OnMission = 3,
    Offline = 4
}

public enum MissionPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public enum MissionState
{
    Assigned = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3
}

public enum GeoEventType
{
    ZoneEntered = 0,
    ZoneExited = 1,
    MissionAssigned = 2,
    MissionStatusChanged = 3,
    VehicleOffline = 4,
    VehicleOnline = 5
}
