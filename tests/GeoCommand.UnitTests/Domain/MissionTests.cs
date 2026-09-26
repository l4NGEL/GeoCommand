using GeoCommand.Domain.Common;
using GeoCommand.Domain.Missions;
using GeoCommand.Domain.Vehicles;

namespace GeoCommand.UnitTests.Domain;

public class MissionTests
{
    [Fact]
    public void Assign_sets_fields_and_puts_vehicle_on_mission()
    {
        var vehicle = TestData.VehicleAt(39.92, 32.85);

        var mission = Mission.Assign(vehicle, "  Kuzey kapısında devriye  ", MissionPriority.High, TestData.Now);

        Assert.Equal("Kuzey kapısında devriye", mission.Description);
        Assert.Equal(MissionStatus.Assigned, mission.Status);
        Assert.Equal(MissionPriority.High, mission.Priority);
        Assert.Equal(TestData.Now, mission.AssignedAtUtc);
        Assert.Equal(mission.Id, vehicle.ActiveMissionId);
        Assert.Equal(VehicleStatus.OnMission, vehicle.Status);
    }

    [Fact]
    public void Vehicle_cannot_have_two_active_missions()
    {
        var vehicle = TestData.VehicleAt(39.92, 32.85);
        Mission.Assign(vehicle, "Birinci", MissionPriority.Normal, TestData.Now);

        Assert.Throws<DomainRuleException>(() => Mission.Assign(vehicle, "İkinci", MissionPriority.Normal, TestData.Now));
    }

    [Fact]
    public void Offline_vehicle_cannot_be_assigned()
    {
        var vehicle = TestData.VehicleAt(39.92, 32.85);
        vehicle.MarkOfflineIfStale(TestData.Now, TimeSpan.FromSeconds(1));

        var ex = Assert.Throws<DomainRuleException>(() => Mission.Assign(vehicle, "Görev", MissionPriority.Low, TestData.Now));
        Assert.Contains("çevrimdışı", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Empty_description_is_rejected(string description)
    {
        var vehicle = TestData.VehicleAt(39.92, 32.85);

        Assert.Throws<DomainValidationException>(() => Mission.Assign(vehicle, description, MissionPriority.Low, TestData.Now));
        Assert.Null(vehicle.ActiveMissionId);
    }

    [Fact]
    public void Too_long_description_and_undefined_priority_are_both_reported()
    {
        var vehicle = TestData.VehicleAt(39.92, 32.85);

        var ex = Assert.Throws<DomainValidationException>(() =>
            Mission.Assign(vehicle, new string('x', Mission.MaxDescriptionLength + 1), (MissionPriority)99, TestData.Now));

        Assert.Equal(2, ex.Errors.Count);
    }

    [Fact]
    public void Completing_mission_frees_vehicle_for_next_assignment()
    {
        var vehicle = TestData.VehicleAt(39.92, 32.85);
        var mission = Mission.Assign(vehicle, "Birinci", MissionPriority.Normal, TestData.Now);

        mission.ChangeStatus(MissionStatus.InProgress, vehicle, TestData.Now.AddMinutes(1));
        mission.ChangeStatus(MissionStatus.Completed, vehicle, TestData.Now.AddMinutes(5));

        Assert.Equal(MissionStatus.Completed, mission.Status);
        Assert.Equal(TestData.Now.AddMinutes(5), mission.UpdatedAtUtc);
        Assert.Null(vehicle.ActiveMissionId);
        Assert.NotEqual(VehicleStatus.OnMission, vehicle.Status);
        Mission.Assign(vehicle, "İkinci", MissionPriority.Normal, TestData.Now.AddMinutes(6));
    }

    [Theory]
    [InlineData(MissionStatus.Assigned, MissionStatus.Completed)]
    [InlineData(MissionStatus.Completed, MissionStatus.InProgress)]
    [InlineData(MissionStatus.Cancelled, MissionStatus.Assigned)]
    [InlineData(MissionStatus.Completed, MissionStatus.Cancelled)]
    public void Invalid_transitions_are_rejected(MissionStatus from, MissionStatus to)
    {
        var vehicle = TestData.VehicleAt(39.92, 32.85);
        var mission = Mission.Assign(vehicle, "Test", MissionPriority.Normal, TestData.Now);
        if (from == MissionStatus.Completed)
        {
            mission.ChangeStatus(MissionStatus.InProgress, vehicle, TestData.Now);
            mission.ChangeStatus(MissionStatus.Completed, vehicle, TestData.Now);
        }
        else if (from == MissionStatus.Cancelled)
        {
            mission.ChangeStatus(MissionStatus.Cancelled, vehicle, TestData.Now);
        }

        Assert.Throws<DomainRuleException>(() => mission.ChangeStatus(to, vehicle, TestData.Now));
    }

    [Fact]
    public void Cancelling_mission_of_offline_vehicle_keeps_it_offline()
    {
        var vehicle = TestData.VehicleAt(39.92, 32.85);
        var mission = Mission.Assign(vehicle, "Test", MissionPriority.Normal, TestData.Now);
        vehicle.MarkOfflineIfStale(TestData.Now, TimeSpan.FromSeconds(1));

        mission.ChangeStatus(MissionStatus.Cancelled, vehicle, TestData.Now);

        Assert.Equal(VehicleStatus.Offline, vehicle.Status);
        Assert.Null(vehicle.ActiveMissionId);
    }
}
