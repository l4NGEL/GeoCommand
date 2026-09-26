using System.Collections.ObjectModel;
using GeoCommand.Contracts;

namespace GeoCommand.Desktop.ViewModels;

/// <summary>
/// İstemcinin gördüğü operasyon durumu. REST anlık görüntüsü ile SignalR canlı güncellemelerini birleştirir:
/// <list type="bullet">
/// <item>Araç güncellemesi, eldeki kayıttan eskiyse uygulanmaz (yeniden bağlanma sırasında sıra karışabilir).</item>
/// <item>Olaylar kimliğe göre tekilleştirilir; anlık görüntüde de canlı akışta da gelen olay bir kez görünür.</item>
/// </list>
/// WPF'e bağımlı değildir; UI iş parçacığından çağrılmalıdır.
/// </summary>
public sealed class OperationsState
{
    public const int MaxEvents = 500;

    private readonly HashSet<Guid> _eventIds = [];

    public ObservableCollection<VehicleItemViewModel> Vehicles { get; } = [];
    /// <summary>En yeni olay en üstte.</summary>
    public ObservableCollection<GeoEventDto> Events { get; } = [];
    public ObservableCollection<ZoneDto> Zones { get; } = [];

    public VehicleItemViewModel? FindVehicle(Guid id) => Vehicles.FirstOrDefault(v => v.Id == id);

    /// <returns>Güncelleme uygulandıysa <c>true</c>; eski (sırası bozuk) güncellemelerde <c>false</c>.</returns>
    public bool ApplyVehicle(VehicleDto dto)
    {
        var existing = FindVehicle(dto.Id);
        if (existing is null)
        {
            InsertSorted(new VehicleItemViewModel(dto));
            return true;
        }

        if (IsOlder(dto.LastUpdateUtc, existing.LastUpdateUtc)) return false;
        existing.Apply(dto);
        return true;
    }

    /// <returns>Olay yeni ise <c>true</c>; daha önce görüldüyse <c>false</c>.</returns>
    public bool AddEvent(GeoEventDto geoEvent)
    {
        if (!_eventIds.Add(geoEvent.Id)) return false;

        var index = 0;
        while (index < Events.Count && Events[index].OccurredAtUtc > geoEvent.OccurredAtUtc) index++;
        Events.Insert(index, geoEvent);

        while (Events.Count > MaxEvents)
        {
            _eventIds.Remove(Events[^1].Id);
            Events.RemoveAt(Events.Count - 1);
        }
        return true;
    }

    public void UpsertZone(ZoneDto zone)
    {
        var index = IndexOfZone(zone.Id);
        if (index >= 0) Zones[index] = zone;
        else Zones.Add(zone);
    }

    public void RemoveZone(Guid zoneId)
    {
        var index = IndexOfZone(zoneId);
        if (index >= 0) Zones.RemoveAt(index);
    }

    /// <summary>
    /// Yeniden bağlanma sonrası REST'ten alınan güncel durumu uygular. Bu arada canlı akıştan daha yeni bir
    /// araç güncellemesi geldiyse korunur; silinen bölgeler kaldırılır; olaylar birleştirilir.
    /// </summary>
    /// <returns>Anlık görüntüyle eklenen (bağlantı kopukken kaçırılmış) olay sayısı.</returns>
    public int ApplySnapshot(IReadOnlyList<VehicleDto> vehicles, IReadOnlyList<GeoEventDto> events, IReadOnlyList<ZoneDto> zones)
    {
        foreach (var vehicle in vehicles) ApplyVehicle(vehicle);
        var known = vehicles.Select(v => v.Id).ToHashSet();
        foreach (var gone in Vehicles.Where(v => !known.Contains(v.Id)).ToList()) Vehicles.Remove(gone);

        var zoneIds = zones.Select(z => z.Id).ToHashSet();
        foreach (var gone in Zones.Where(z => !zoneIds.Contains(z.Id)).ToList()) Zones.Remove(gone);
        foreach (var zone in zones) UpsertZone(zone);

        return events.Count(AddEvent);
    }

    private static bool IsOlder(DateTimeOffset? candidate, DateTimeOffset? current) =>
        candidate is not null && current is not null && candidate < current;

    private void InsertSorted(VehicleItemViewModel vehicle)
    {
        var index = 0;
        while (index < Vehicles.Count && string.CompareOrdinal(Vehicles[index].Callsign, vehicle.Callsign) < 0) index++;
        Vehicles.Insert(index, vehicle);
    }

    private int IndexOfZone(Guid id)
    {
        for (var i = 0; i < Zones.Count; i++)
            if (Zones[i].Id == id) return i;
        return -1;
    }
}
