using System.Runtime.CompilerServices;
using GeoCommand.Sdk;
using GeoCommand.Infrastructure.DataSources.Simulation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoCommand.Infrastructure.DataSources;

/// <summary>data/scenarios altındaki JSON senaryolarını gerçek zamanlı olarak oynatan simülatör.</summary>
public sealed class SimulatedPositionSource(
    IOptions<DataSourceOptions> options,
    TimeProvider time,
    ILogger<SimulatedPositionSource> logger) : IPositionSource
{
    public const string TypeName = "Simulator";
    public const string ReportSource = "simulator";

    private string Directory => PathResolver.Resolve(options.Value.Simulator.ScenarioDirectory);

    public string SourceType => TypeName;

    public string DefaultScenario => options.Value.Simulator.DefaultScenario;

    public IReadOnlyList<string> AvailableScenarios =>
        System.IO.Directory.Exists(Directory)
            ? System.IO.Directory.GetFiles(Directory, "*.json").Select(Path.GetFileNameWithoutExtension).OfType<string>().Order().ToList()
            : [];

    public ScenarioDefinition LoadScenario(string scenario)
    {
        // Yalnızca listelenen senaryolar kabul edilir; istemci keyfi dosya yolu veremez.
        if (!AvailableScenarios.Contains(scenario, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"'{scenario}' adlı senaryo yok. Mevcut: {string.Join(", ", AvailableScenarios)}.");

        var path = Path.Combine(Directory, scenario + ".json");
        return ScenarioDefinition.Parse(File.ReadAllText(path), Path.GetFileName(path));
    }

    public async IAsyncEnumerable<PositionReport> ReadAsync(string scenario, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var definition = LoadScenario(scenario);
        var runner = new ScenarioRunner(definition);
        logger.LogInformation("Simülasyon başladı. Senaryo={Scenario} Seed={Seed} Araç={VehicleCount} Tick={TickSeconds}s",
            definition.Name, definition.Seed, definition.Vehicles.Count, definition.TickSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(runner.TickSeconds), time);
        do
        {
            var now = time.GetUtcNow();
            foreach (var fix in runner.Step())
                yield return new PositionReport(fix.Callsign, fix.Latitude, fix.Longitude, fix.SpeedMps, fix.HeadingDegrees, now, ReportSource);
        }
        while (await timer.WaitForNextTickAsync(cancellationToken));
    }
}
