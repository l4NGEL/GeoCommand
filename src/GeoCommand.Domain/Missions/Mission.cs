using GeoCommand.Domain.Common;
using GeoCommand.Domain.Vehicles;

namespace GeoCommand.Domain.Missions;

public enum MissionPriority
{
    Low = 0,
    Normal = 1,
    High = 2,
    Critical = 3
}

public enum MissionStatus
{
    Assigned = 0,
    InProgress = 1,
    Completed = 2,
    Cancelled = 3
}

/// <summary>Bir araca atanan simülasyon görevi. Bir aracın aynı anda tek aktif görevi olabilir.</summary>
public sealed class Mission
{
    public const int MaxDescriptionLength = 500;

    public Guid Id { get; private set; }
    public Guid VehicleId { get; private set; }
    public string Description { get; private set; } = null!;
    public MissionPriority Priority { get; private set; }
    public MissionStatus Status { get; private set; }
    public DateTimeOffset AssignedAtUtc { get; private set; }
    public DateTimeOffset UpdatedAtUtc { get; private set; }

    public bool IsActive => Status is MissionStatus.Assigned or MissionStatus.InProgress;

    private Mission() { } // EF Core

    public static Mission Assign(Vehicle vehicle, string? description, MissionPriority priority, DateTimeOffset now)
    {
        var errors = new List<string>();
        description = description?.Trim() ?? "";
        if (description.Length == 0) errors.Add("Görev açıklaması zorunludur.");
        else if (description.Length > MaxDescriptionLength) errors.Add($"Görev açıklaması en fazla {MaxDescriptionLength} karakter olabilir.");
        if (!Enum.IsDefined(priority)) errors.Add($"Geçersiz öncelik: {(int)priority}.");
        if (errors.Count > 0) throw new DomainValidationException(errors);

        if (vehicle.Status == VehicleStatus.Offline)
            throw new DomainRuleException($"{vehicle.Callsign} çevrimdışı; çevrimdışı araca görev atanamaz.");
        if (vehicle.ActiveMissionId is not null)
            throw new DomainRuleException($"{vehicle.Callsign} aracının zaten aktif bir görevi var. Önce onu tamamlayın veya iptal edin.");

        var mission = new Mission
        {
            Id = Guid.NewGuid(),
            VehicleId = vehicle.Id,
            Description = description,
            Priority = priority,
            Status = MissionStatus.Assigned,
            AssignedAtUtc = now.ToUniversalTime(),
            UpdatedAtUtc = now.ToUniversalTime()
        };
        vehicle.AttachMission(mission.Id);
        return mission;
    }

    public void ChangeStatus(MissionStatus target, Vehicle vehicle, DateTimeOffset now)
    {
        if (vehicle.Id != VehicleId) throw new InvalidOperationException("Görev bu araca ait değil.");
        if (!Enum.IsDefined(target)) throw new DomainValidationException([$"Geçersiz görev durumu: {(int)target}."]);

        var allowed = (Status, target) switch
        {
            (MissionStatus.Assigned, MissionStatus.InProgress) => true,
            (MissionStatus.Assigned, MissionStatus.Cancelled) => true,
            (MissionStatus.InProgress, MissionStatus.Completed) => true,
            (MissionStatus.InProgress, MissionStatus.Cancelled) => true,
            _ => false
        };
        if (!allowed)
            throw new DomainRuleException($"Görev '{Status.ToTurkish()}' durumundayken '{target.ToTurkish()}' durumuna geçilemez.");

        Status = target;
        UpdatedAtUtc = now.ToUniversalTime();
        if (!IsActive) vehicle.DetachMission(Id);
    }
}
