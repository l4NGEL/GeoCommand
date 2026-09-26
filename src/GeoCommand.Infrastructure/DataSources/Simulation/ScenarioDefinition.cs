using System.Text.Json;
using System.Text.Json.Serialization;
using GeoCommand.Domain.Common;
using GeoCommand.Domain.Vehicles;

namespace GeoCommand.Infrastructure.DataSources.Simulation;

/// <summary>data/scenarios/*.json dosyalarının biçimi. Aynı <see cref="Seed"/> her çalıştırmada aynı rotayı üretir.</summary>
public sealed class ScenarioDefinition
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public int Seed { get; set; }
    public double TickSeconds { get; set; } = 1.0;
    public List<ScenarioVehicle> Vehicles { get; set; } = [];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        NumberHandling = JsonNumberHandling.Strict
    };

    public static ScenarioDefinition Parse(string json, string sourceName)
    {
        ScenarioDefinition? definition;
        try
        {
            definition = JsonSerializer.Deserialize<ScenarioDefinition>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{sourceName}: JSON okunamadı ({ex.Message}).", ex);
        }
        if (definition is null) throw new InvalidDataException($"{sourceName}: dosya boş.");

        var errors = definition.Validate();
        if (errors.Count > 0) throw new InvalidDataException($"{sourceName}: {string.Join(" ", errors)}");
        return definition;
    }

    public List<string> Validate()
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("Senaryo adı zorunlu.");
        if (TickSeconds is < 0.2 or > 60) errors.Add("tickSeconds 0.2-60 arasında olmalı.");
        if (Vehicles.Count == 0) errors.Add("En az bir araç tanımlanmalı.");

        foreach (var (v, i) in Vehicles.Select((v, i) => (v, i + 1)))
        {
            var prefix = $"Araç {i} ({v.Callsign}): ";
            if (string.IsNullOrWhiteSpace(v.Callsign)) errors.Add($"{prefix}callsign zorunlu.");
            if (v.SpeedMps is <= 0 or > PositionFix.MaxSpeedMps) errors.Add($"{prefix}speedMps 0-{PositionFix.MaxSpeedMps} arasında olmalı.");
            if (v.SpeedJitter is < 0 or > 0.5) errors.Add($"{prefix}speedJitter 0-0.5 arasında olmalı.");
            if (v.DwellSeconds < 0) errors.Add($"{prefix}dwellSeconds negatif olamaz.");
            if (v.Route.Count < 2) errors.Add($"{prefix}rota en az 2 noktadan oluşmalı.");
            foreach (var p in v.Route)
            {
                if (p.Length != 2) { errors.Add($"{prefix}rota noktaları [enlem, boylam] biçiminde olmalı."); break; }
                Geo.AddCoordinateErrors(errors, p[0], p[1], prefix);
            }
        }

        var duplicates = Vehicles.GroupBy(v => v.Callsign.Trim().ToUpperInvariant()).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        if (duplicates.Count > 0) errors.Add($"Yinelenen çağrı adları: {string.Join(", ", duplicates)}.");
        return errors;
    }
}

public sealed class ScenarioVehicle
{
    public string Callsign { get; set; } = "";
    public double SpeedMps { get; set; }

    /// <summary>Her adımda hızın ± bu oranda rastgele (seed'e bağlı) değişmesi. 0.1 = ±%10.</summary>
    public double SpeedJitter { get; set; } = 0.1;

    /// <summary>Her rota noktasında bekleme süresi; beklerken hız 0 bildirilir.</summary>
    public double DwellSeconds { get; set; }

    /// <summary>true: son noktadan ilk noktaya dönülür ve rota tekrarlanır. false: son noktada durur.</summary>
    public bool Loop { get; set; } = true;

    /// <summary>[enlem, boylam] çiftleri.</summary>
    public List<double[]> Route { get; set; } = [];
}
