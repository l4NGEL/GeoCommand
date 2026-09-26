using GeoCommand.Application.Queries;
using GeoCommand.Infrastructure.DataSources.Simulation;
using GeoCommand.Infrastructure.Persistence;

namespace GeoCommand.UnitTests.DataSources;

public class ScenarioRunnerTests
{
    private static ScenarioDefinition Scenario(int seed = 1, double dwell = 0, bool loop = true, double jitter = 0.1) => new()
    {
        Name = "test",
        Seed = seed,
        TickSeconds = 1,
        Vehicles =
        [
            new ScenarioVehicle
            {
                Callsign = "ALFA-1", SpeedMps = 20, SpeedJitter = jitter, DwellSeconds = dwell, Loop = loop,
                Route = [[39.9208, 32.8440], [39.9208, 32.8640], [39.9300, 32.8640]]
            },
            new ScenarioVehicle
            {
                Callsign = "BRAVO-2", SpeedMps = 10, Route = [[39.91, 32.85], [39.92, 32.85]]
            }
        ]
    };

    private static List<SimulatedFix> Run(ScenarioDefinition scenario, int steps)
    {
        var runner = new ScenarioRunner(scenario);
        return Enumerable.Range(0, steps).SelectMany(_ => runner.Step()).ToList();
    }

    [Fact]
    public void Same_seed_produces_identical_sequence()
    {
        Assert.Equal(Run(Scenario(seed: 42), 500), Run(Scenario(seed: 42), 500));
    }

    [Fact]
    public void Different_seed_produces_different_sequence()
    {
        Assert.NotEqual(Run(Scenario(seed: 1), 50), Run(Scenario(seed: 2), 50));
    }

    [Fact]
    public void Each_step_reports_every_vehicle_with_speed_within_jitter()
    {
        var runner = new ScenarioRunner(Scenario());

        for (var i = 0; i < 100; i++)
        {
            var fixes = runner.Step();
            Assert.Equal(["ALFA-1", "BRAVO-2"], fixes.Select(f => f.Callsign));
            Assert.InRange(fixes[0].SpeedMps, 20 * 0.9 - 0.01, 20 * 1.1 + 0.01);
        }
    }

    [Fact]
    public void Distance_between_steps_matches_reported_speed()
    {
        var fixes = Run(Scenario(jitter: 0), 30).Where(f => f.Callsign == "ALFA-1").ToList();

        for (var i = 1; i < 20; i++) // ilk bacak ~1,7 km; köşeye gelmeden ölç
        {
            var meters = GeoMath.HaversineMeters(fixes[i - 1].Latitude, fixes[i - 1].Longitude, fixes[i].Latitude, fixes[i].Longitude);
            Assert.InRange(meters, 19.5, 20.5);
        }
    }

    [Fact]
    public void Heading_follows_route_segment_direction()
    {
        var first = new ScenarioRunner(Scenario()).Step()[0];

        Assert.InRange(first.HeadingDegrees, 89, 91);     // doğuya
        var bravo = new ScenarioRunner(Scenario()).Step()[1];
        Assert.True(bravo.HeadingDegrees is < 1 or > 359); // kuzeye
    }

    [Fact]
    public void Dwell_reports_zero_speed_at_waypoints()
    {
        var fixes = Run(Scenario(dwell: 5), 200).Where(f => f.Callsign == "ALFA-1").ToList();

        Assert.Contains(fixes, f => f.SpeedMps == 0);
    }

    [Fact]
    public void Non_looping_vehicle_stops_at_last_point()
    {
        var last = Run(Scenario(loop: false), 400).Last(f => f.Callsign == "ALFA-1");

        Assert.Equal(0, last.SpeedMps);
        Assert.Equal(39.93, last.Latitude, 4);
        Assert.Equal(32.864, last.Longitude, 4);
    }

    [Fact]
    public void Validation_reports_all_problems()
    {
        var bad = new ScenarioDefinition
        {
            Name = "",
            TickSeconds = 0,
            Vehicles =
            [
                new ScenarioVehicle { Callsign = "A-1", SpeedMps = 0, Route = [[95, 32]] },
                new ScenarioVehicle { Callsign = "a-1", SpeedMps = 5, Route = [[39, 32], [39.1, 32]] }
            ]
        };

        var errors = bad.Validate();

        Assert.Contains(errors, e => e.Contains("Senaryo adı"));
        Assert.Contains(errors, e => e.Contains("tickSeconds"));
        Assert.Contains(errors, e => e.Contains("speedMps"));
        Assert.Contains(errors, e => e.Contains("en az 2"));
        Assert.Contains(errors, e => e.Contains("Enlem"));
        Assert.Contains(errors, e => e.Contains("Yinelenen"));
    }

    [Fact]
    public void Parse_reports_file_name_for_invalid_json()
    {
        var ex = Assert.Throws<InvalidDataException>(() => ScenarioDefinition.Parse("{ \"name\": ", "bozuk.json"));

        Assert.StartsWith("bozuk.json", ex.Message);
    }

    [Theory]
    [MemberData(nameof(ShippedScenarios))]
    public void Shipped_scenarios_are_valid_and_use_seeded_vehicles(string path)
    {
        var scenario = ScenarioDefinition.Parse(File.ReadAllText(path), Path.GetFileName(path));
        var seeded = SeedData.Vehicles.Select(v => v.Callsign).ToHashSet();

        Assert.True(scenario.Vehicles.Count >= 3);
        Assert.All(scenario.Vehicles, v => Assert.Contains(v.Callsign, seeded));
    }

    public static TheoryData<string> ShippedScenarios() =>
        new(Directory.GetFiles(Path.Combine(RepoPaths.DataDirectory, "scenarios"), "*.json"));
}
