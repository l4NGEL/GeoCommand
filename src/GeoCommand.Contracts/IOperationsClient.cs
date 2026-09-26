namespace GeoCommand.Contracts;

/// <summary>
/// Sunucunun SignalR üzerinden bağlı istemcilere gönderdiği mesajlar. Metot adları istemcide
/// <c>connection.On(nameof(IOperationsClient.VehicleUpdated), ...)</c> şeklinde kullanılır.
/// </summary>
public interface IOperationsClient
{
    Task VehicleUpdated(VehicleDto vehicle);
    Task EventRaised(GeoEventDto geoEvent);
    Task MissionChanged(MissionDto mission);
    Task ZoneCreated(ZoneDto zone);
    Task ZoneDeleted(Guid zoneId);
    Task SourceStatusChanged(SourceStatusDto status);
}

public static class HubRoutes
{
    public const string Operations = "/hubs/operations";
}
