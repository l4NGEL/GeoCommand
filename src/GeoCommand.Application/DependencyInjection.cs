using GeoCommand.Application.Abstractions;
using GeoCommand.Application.Ingestion;
using GeoCommand.Application.Liveness;
using GeoCommand.Application.Missions;
using GeoCommand.Application.Queries;
using GeoCommand.Application.Zones;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace GeoCommand.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<LivenessOptions>()
            .Bind(configuration.GetSection(LivenessOptions.Section))
            .Validate(o => o.OfflineAfterSeconds > 0 && o.CheckIntervalSeconds > 0,
                "Liveness:OfflineAfterSeconds ve Liveness:CheckIntervalSeconds pozitif olmalı.")
            .ValidateOnStart();

        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<VehicleLocks>();
        services.AddScoped<PositionIngestionService>();
        services.AddScoped<OperationsQueries>();
        services.AddScoped<ZoneService>();
        services.AddScoped<MissionService>();
        services.AddScoped<VehicleLivenessService>();
        return services;
    }
}
