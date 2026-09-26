using GeoCommand.Application.Liveness;
using Microsoft.Extensions.Options;

namespace GeoCommand.Api.Hosting;

/// <summary>Belirli aralıklarla bildirim göndermeyen araçları çevrimdışı işaretler.</summary>
internal sealed class LivenessMonitor(
    IServiceScopeFactory scopes,
    IOptions<LivenessOptions> options,
    TimeProvider time,
    ILogger<LivenessMonitor> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(options.Value.CheckIntervalSeconds), time);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<VehicleLivenessService>().MarkStaleVehiclesAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(ex, "Çevrimdışı araç kontrolü başarısız oldu.");
            }
        }
    }
}
