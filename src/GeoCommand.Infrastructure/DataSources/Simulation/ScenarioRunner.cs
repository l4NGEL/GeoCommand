using GeoCommand.Application.Queries;

namespace GeoCommand.Infrastructure.DataSources.Simulation;

public sealed record SimulatedFix(string Callsign, double Latitude, double Longitude, double SpeedMps, double HeadingDegrees);

/// <summary>
/// Saatten bağımsız, saf simülasyon motoru: her <see cref="Step"/> çağrısı araçları bir tick ilerletir.
/// Tüm rastgelelik senaryodaki seed'den gelir, bu yüzden aynı senaryo her zaman aynı konum dizisini üretir.
/// Rota bacakları kısa (birkaç km) olduğu için bacak içinde enlem/boylamda doğrusal ara değerleme yeterlidir;
/// mesafeler haversine ile metre cinsinden hesaplanır.
/// </summary>
public sealed class ScenarioRunner
{
    private readonly ScenarioDefinition _definition;
    private readonly Random _random;
    private readonly VehicleState[] _states;

    public ScenarioRunner(ScenarioDefinition definition)
    {
        _definition = definition;
        _random = new Random(definition.Seed);
        _states = definition.Vehicles.Select(v => new VehicleState(v)).ToArray();
    }

    public double TickSeconds => _definition.TickSeconds;

    public IReadOnlyList<SimulatedFix> Step()
    {
        var fixes = new List<SimulatedFix>(_states.Length);
        foreach (var state in _states)
            fixes.Add(state.Advance(_definition.TickSeconds, _random));
        return fixes;
    }

    private sealed class VehicleState(ScenarioVehicle vehicle)
    {
        private int _segment;              // _route[_segment] -> _route[_segment + 1]
        private double _metersIntoSegment;
        private double _dwellRemaining;
        private bool _finished;
        private double _lastHeading = InitialHeading(vehicle);

        private IReadOnlyList<double[]> Route { get; } = vehicle.Loop
            ? [.. vehicle.Route, vehicle.Route[0]]
            : vehicle.Route;

        public SimulatedFix Advance(double tickSeconds, Random random)
        {
            // Rastgele sayı her adımda (beklerken de) tüketilir; böylece dizinin tekrarlanabilirliği
            // aracın o anki durumuna bağlı kalmaz.
            var jitter = 1 + vehicle.SpeedJitter * (2 * random.NextDouble() - 1);

            if (_finished || _dwellRemaining > 0)
            {
                _dwellRemaining = Math.Max(0, _dwellRemaining - tickSeconds);
                return Current(0);
            }

            var speed = vehicle.SpeedMps * jitter;
            var remaining = speed * tickSeconds;
            while (remaining > 0 && !_finished)
            {
                var length = SegmentLength();
                var left = length - _metersIntoSegment;
                if (remaining < left)
                {
                    _metersIntoSegment += remaining;
                    remaining = 0;
                }
                else
                {
                    remaining -= left;
                    NextSegment();
                    if (vehicle.DwellSeconds > 0) { _dwellRemaining = vehicle.DwellSeconds; break; }
                }
            }
            return Current(speed);
        }

        private void NextSegment()
        {
            _metersIntoSegment = 0;
            _segment++;
            if (_segment < Route.Count - 1) return;

            if (vehicle.Loop) _segment = 0;
            else { _segment = Route.Count - 2; _metersIntoSegment = SegmentLength(); _finished = true; }
        }

        private double SegmentLength()
        {
            var (a, b) = (Route[_segment], Route[_segment + 1]);
            return GeoMath.HaversineMeters(a[0], a[1], b[0], b[1]);
        }

        private SimulatedFix Current(double speed)
        {
            var (a, b) = (Route[_segment], Route[_segment + 1]);
            var length = SegmentLength();
            var t = length <= 0 ? 0 : Math.Clamp(_metersIntoSegment / length, 0, 1);
            var lat = a[0] + (b[0] - a[0]) * t;
            var lon = a[1] + (b[1] - a[1]) * t;
            if (speed > 0) _lastHeading = Bearing(a[0], a[1], b[0], b[1]);
            return new SimulatedFix(vehicle.Callsign, Math.Round(lat, 7), Math.Round(lon, 7), Math.Round(speed, 2), Math.Round(_lastHeading, 1) % 360);
        }

        private static double InitialHeading(ScenarioVehicle v) =>
            Bearing(v.Route[0][0], v.Route[0][1], v.Route[1][0], v.Route[1][1]);

        /// <summary>İlk büyük daire kerteriz açısı, 0-360 derece (kuzey = 0).</summary>
        private static double Bearing(double lat1, double lon1, double lat2, double lon2)
        {
            double R(double d) => d * Math.PI / 180;
            var y = Math.Sin(R(lon2 - lon1)) * Math.Cos(R(lat2));
            var x = Math.Cos(R(lat1)) * Math.Sin(R(lat2)) - Math.Sin(R(lat1)) * Math.Cos(R(lat2)) * Math.Cos(R(lon2 - lon1));
            return (Math.Atan2(y, x) * 180 / Math.PI + 360) % 360;
        }
    }
}
