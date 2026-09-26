namespace GeoCommand.Infrastructure.Persistence;

/// <summary>Migration ile eklenen simüle araçlar. Senaryo ve kayıt dosyaları bu çağrı adlarını kullanır.</summary>
public static class SeedData
{
    public sealed record SeedVehicle(Guid Id, string Callsign);

    public static readonly IReadOnlyList<SeedVehicle> Vehicles =
    [
        new(Guid.Parse("0f1c7a52-6a3e-4c1e-9d11-0000000000a1"), "ALFA-1"),
        new(Guid.Parse("0f1c7a52-6a3e-4c1e-9d11-0000000000b2"), "BRAVO-2"),
        new(Guid.Parse("0f1c7a52-6a3e-4c1e-9d11-0000000000c3"), "CHARLIE-3"),
        new(Guid.Parse("0f1c7a52-6a3e-4c1e-9d11-0000000000d4"), "DELTA-4")
    ];
}
