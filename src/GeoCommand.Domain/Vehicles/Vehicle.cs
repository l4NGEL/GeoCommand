using GeoCommand.Domain.Common;
using NetTopologySuite.Geometries;

namespace GeoCommand.Domain.Vehicles;

public sealed class Vehicle
{
    /// <summary>Bu hızın altındaki araç "beklemede" kabul edilir.</summary>
    public const double MovingThresholdMps = 0.5;

    public Guid Id { get; private set; }
    public string Callsign { get; private set; } = null!;
    public VehicleStatus Status { get; private set; }
    public Point? LastPosition { get; private set; }
    public double LastSpeedMps { get; private set; }
    public double LastHeadingDegrees { get; private set; }
    public DateTimeOffset? LastUpdateUtc { get; private set; }
    public Guid? ActiveMissionId { get; private set; }

    private Vehicle() { } // EF Core

    public Vehicle(Guid id, string callsign)
    {
        if (id == Guid.Empty) throw new DomainValidationException(["Araç kimliği boş olamaz."]);
        Id = id;
        Callsign = NormalizeCallsign(callsign);
        Status = VehicleStatus.Unknown;
    }

    public static string NormalizeCallsign(string? callsign)
    {
        var normalized = callsign?.Trim().ToUpperInvariant() ?? "";
        if (normalized.Length is < 2 or > 32)
            throw new DomainValidationException(["Çağrı adı 2-32 karakter olmalı."]);
        return normalized;
    }

    /// <summary>
    /// Yinelenen veya sırası bozuk bildirimler reddedilir. Aynı bildirimin tekrar gönderilmesi
    /// (ör. yeniden bağlanma sonrası) bu sayede ikinci kez bölge olayı üretemez.
    /// </summary>
    public bool IsNewerThanLastFix(PositionFix fix) =>
        LastUpdateUtc is null || fix.TimestampUtc > LastUpdateUtc.Value;

    /// <returns>Araç bu bildirimle çevrimdışı durumdan döndüyse <c>true</c>.</returns>
    public bool ApplyFix(PositionFix fix)
    {
        if (!IsNewerThanLastFix(fix))
            throw new DomainRuleException(
                $"{Callsign} için bildirim zamanı ({fix.TimestampUtc:O}) son kayıttan ({LastUpdateUtc:O}) yeni değil.");

        var wasOffline = Status == VehicleStatus.Offline;

        LastPosition = Geo.CreatePoint(fix.Latitude, fix.Longitude);
        LastSpeedMps = fix.SpeedMps;
        LastHeadingDegrees = fix.HeadingDegrees;
        LastUpdateUtc = fix.TimestampUtc;
        Status = CalculateOnlineStatus();

        return wasOffline;
    }

    /// <returns>Araç bu çağrıyla çevrimdışı durumuna geçtiyse <c>true</c>.</returns>
    public bool MarkOfflineIfStale(DateTimeOffset now, TimeSpan threshold)
    {
        if (Status is VehicleStatus.Offline or VehicleStatus.Unknown || LastUpdateUtc is null) return false;
        if (now - LastUpdateUtc.Value <= threshold) return false;

        Status = VehicleStatus.Offline;
        return true;
    }

    internal void AttachMission(Guid missionId)
    {
        ActiveMissionId = missionId;
        if (Status != VehicleStatus.Offline) Status = CalculateOnlineStatus();
    }

    internal void DetachMission(Guid missionId)
    {
        if (ActiveMissionId != missionId) return;
        ActiveMissionId = null;
        if (Status != VehicleStatus.Offline) Status = CalculateOnlineStatus();
    }

    private VehicleStatus CalculateOnlineStatus()
    {
        if (LastPosition is null) return VehicleStatus.Unknown;
        if (ActiveMissionId is not null) return VehicleStatus.OnMission;
        return LastSpeedMps >= MovingThresholdMps ? VehicleStatus.Moving : VehicleStatus.Idle;
    }
}
