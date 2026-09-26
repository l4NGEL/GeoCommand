using GeoCommand.Contracts;
using GeoCommand.Desktop.ViewModels;

namespace GeoCommand.Desktop.Tests;

/// <summary>Yeniden bağlanma sonrası eşitleme ve yinelenen olay önleme kuralları.</summary>
public class OperationsStateTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 26, 10, 0, 0, TimeSpan.Zero);
    private static readonly Guid Alfa = Guid.NewGuid();
    private static readonly Guid Bravo = Guid.NewGuid();

    private static VehicleDto Vehicle(Guid id, string callsign, double lat, DateTimeOffset at, VehicleState status = VehicleState.Moving) =>
        new(id, callsign, status, lat, 32.85, 10, 90, at, null);

    private static GeoEventDto Event(Guid id, DateTimeOffset at, GeoEventType type = GeoEventType.ZoneEntered) =>
        new(id, type, Alfa, "ALFA-1", Guid.NewGuid(), null, at, "olay", 39.9, 32.8);

    [Fact]
    public void Vehicles_are_kept_sorted_by_callsign()
    {
        var state = new OperationsState();
        state.ApplyVehicle(Vehicle(Bravo, "BRAVO-2", 39.9, T0));
        state.ApplyVehicle(Vehicle(Alfa, "ALFA-1", 39.9, T0));

        Assert.Equal(["ALFA-1", "BRAVO-2"], state.Vehicles.Select(v => v.Callsign));
    }

    [Fact]
    public void Older_live_update_does_not_overwrite_newer_position()
    {
        var state = new OperationsState();
        state.ApplyVehicle(Vehicle(Alfa, "ALFA-1", 39.95, T0.AddSeconds(10)));

        var applied = state.ApplyVehicle(Vehicle(Alfa, "ALFA-1", 39.90, T0.AddSeconds(5)));

        Assert.False(applied);
        Assert.Equal(39.95, state.Vehicles.Single().Latitude);
    }

    [Fact]
    public void Status_change_with_same_timestamp_is_applied()
    {
        // Çevrimdışı işaretleme ve görev atama konum zamanını değiştirmez; yine de uygulanmalı.
        var state = new OperationsState();
        state.ApplyVehicle(Vehicle(Alfa, "ALFA-1", 39.9, T0));

        Assert.True(state.ApplyVehicle(Vehicle(Alfa, "ALFA-1", 39.9, T0, VehicleState.Offline)));
        Assert.Equal(VehicleState.Offline, state.Vehicles.Single().Status);
    }

    [Fact]
    public void Snapshot_does_not_roll_back_a_newer_live_update_received_during_resync()
    {
        var state = new OperationsState();
        // Yeniden bağlandık; REST isteği yoldayken SignalR daha yeni konumu getirdi.
        state.ApplyVehicle(Vehicle(Alfa, "ALFA-1", 39.96, T0.AddSeconds(20)));

        state.ApplySnapshot([Vehicle(Alfa, "ALFA-1", 39.91, T0.AddSeconds(18))], [], []);

        Assert.Equal(39.96, state.Vehicles.Single().Latitude);
    }

    [Fact]
    public void Snapshot_updates_stale_vehicle_state_after_reconnect()
    {
        var state = new OperationsState();
        state.ApplyVehicle(Vehicle(Alfa, "ALFA-1", 39.90, T0));

        state.ApplySnapshot([Vehicle(Alfa, "ALFA-1", 39.99, T0.AddMinutes(2)), Vehicle(Bravo, "BRAVO-2", 39.8, T0.AddMinutes(2))], [], []);

        Assert.Equal(39.99, state.FindVehicle(Alfa)!.Latitude);
        Assert.NotNull(state.FindVehicle(Bravo));
    }

    [Fact]
    public void Same_event_from_live_stream_and_snapshot_is_shown_once()
    {
        var state = new OperationsState();
        var id = Guid.NewGuid();
        Assert.True(state.AddEvent(Event(id, T0)));

        Assert.False(state.AddEvent(Event(id, T0)));
        var missed = state.ApplySnapshot([], [Event(id, T0), Event(Guid.NewGuid(), T0.AddSeconds(1))], []);

        Assert.Equal(1, missed);
        Assert.Equal(2, state.Events.Count);
    }

    [Fact]
    public void Events_are_ordered_newest_first_even_when_they_arrive_out_of_order()
    {
        var state = new OperationsState();
        state.AddEvent(Event(Guid.NewGuid(), T0.AddSeconds(5)));
        state.AddEvent(Event(Guid.NewGuid(), T0.AddSeconds(1)));
        state.AddEvent(Event(Guid.NewGuid(), T0.AddSeconds(9)));

        Assert.Equal([T0.AddSeconds(9), T0.AddSeconds(5), T0.AddSeconds(1)], state.Events.Select(e => e.OccurredAtUtc));
    }

    [Fact]
    public void Event_list_is_capped_and_evicted_ids_can_reappear()
    {
        var state = new OperationsState();
        var oldest = Guid.NewGuid();
        state.AddEvent(Event(oldest, T0));
        for (var i = 1; i <= OperationsState.MaxEvents; i++) state.AddEvent(Event(Guid.NewGuid(), T0.AddSeconds(i)));

        Assert.Equal(OperationsState.MaxEvents, state.Events.Count);
        Assert.DoesNotContain(state.Events, e => e.Id == oldest);
    }

    [Fact]
    public void Snapshot_removes_zones_deleted_while_disconnected()
    {
        var state = new OperationsState();
        var kept = new ZoneDto(Guid.NewGuid(), "A", [new(39, 32), new(39, 33), new(40, 33)], T0);
        var deleted = new ZoneDto(Guid.NewGuid(), "B", [new(39, 32), new(39, 33), new(40, 33)], T0);
        state.UpsertZone(kept);
        state.UpsertZone(deleted);

        state.ApplySnapshot([], [], [kept with { Name = "A (güncel)" }]);

        Assert.Equal(["A (güncel)"], state.Zones.Select(z => z.Name));
    }

    [Fact]
    public void Zone_created_by_us_and_broadcast_back_is_not_duplicated()
    {
        var state = new OperationsState();
        var zone = new ZoneDto(Guid.NewGuid(), "A", [new(39, 32), new(39, 33), new(40, 33)], T0);

        state.UpsertZone(zone); // kendi isteğimizin yanıtı
        state.UpsertZone(zone); // SignalR yayını

        Assert.Single(state.Zones);
    }
}
