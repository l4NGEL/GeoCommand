using GeoCommand.Application.Ingestion;
using GeoCommand.Contracts;
using GeoCommand.Domain.Common;
using GeoCommand.Infrastructure.DataSources;
using Microsoft.Extensions.Options;

namespace GeoCommand.Api.Hosting;

/// <summary>
/// Yapılandırmayla seçilen <see cref="IPositionSource"/>'u (simülatör veya dosya) arka planda çalıştırır ve
/// her bildirimi HTTP ile gelenlerle aynı alma hattından geçirir. Operatör API üzerinden başlatıp durdurabilir.
/// </summary>
public sealed class PositionSourceRunner(
    IPositionSource source,
    IServiceScopeFactory scopes,
    IOperationsClient clients,
    IOptions<DataSourceOptions> options,
    ILogger<PositionSourceRunner> logger) : IHostedService
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private CancellationTokenSource? _cts;
    private Task? _loop;
    private string? _activeScenario;
    private string? _lastError;

    public SourceStatusDto Status => new(
        source.SourceType, _loop is { IsCompleted: false }, _activeScenario, source.AvailableScenarios, _lastError);

    async Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.AutoStart) return;
        try
        {
            await StartSourceAsync(null, cancellationToken);
        }
        catch (Exception ex)
        {
            // Kaynak başlatılamasa da API çalışmaya devam eder; hata durum uç noktasında görünür.
            _lastError = ex.Message;
            logger.LogError(ex, "Veri kaynağı otomatik başlatılamadı.");
        }
    }

    async Task IHostedService.StopAsync(CancellationToken cancellationToken) => await StopSourceAsync(cancellationToken);

    public async Task<SourceStatusDto> StartSourceAsync(string? scenario, CancellationToken cancellationToken)
    {
        scenario = string.IsNullOrWhiteSpace(scenario) ? source.DefaultScenario : scenario.Trim();
        var match = source.AvailableScenarios.FirstOrDefault(s => string.Equals(s, scenario, StringComparison.OrdinalIgnoreCase))
                    ?? throw new DomainValidationException([
                        $"'{scenario}' {source.SourceType} kaynağında bulunamadı. Mevcut: {string.Join(", ", source.AvailableScenarios)}."]);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            _cts = new CancellationTokenSource();
            _activeScenario = match;
            _lastError = null;
            var token = _cts.Token;
            _loop = Task.Run(() => RunAsync(match, token), CancellationToken.None);
        }
        finally
        {
            _gate.Release();
        }

        var status = Status;
        await clients.SourceStatusChanged(status);
        return status;
    }

    public async Task<SourceStatusDto> StopSourceAsync(CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
        }
        finally
        {
            _gate.Release();
        }
        return Status;
    }

    private async Task StopCoreAsync()
    {
        if (_cts is null || _loop is null) return;
        await _cts.CancelAsync();
        try { await _loop; } catch (OperationCanceledException) { }
        _cts.Dispose();
        _cts = null;
    }

    private async Task RunAsync(string scenario, CancellationToken cancellationToken)
    {
        long accepted = 0, rejected = 0;
        try
        {
            await foreach (var report in source.ReadAsync(scenario, cancellationToken))
            {
                await using var scope = scopes.CreateAsyncScope();
                var ingestion = scope.ServiceProvider.GetRequiredService<PositionIngestionService>();
                var result = await ingestion.IngestAsync(report, cancellationToken);
                if (result.Outcome == IngestOutcome.Accepted) accepted++; else rejected++;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _lastError = ex.Message;
            logger.LogError(ex, "Veri kaynağı hata ile durdu. Kaynak={Source} Senaryo={Scenario}", source.SourceType, scenario);
        }
        finally
        {
            logger.LogInformation("Veri kaynağı durdu. Kaynak={Source} Senaryo={Scenario} Kabul={Accepted} Red={Rejected}",
                source.SourceType, scenario, accepted, rejected);
            try
            {
                await clients.SourceStatusChanged(Status with { IsRunning = false });
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Kaynak durumu yayınlanamadı.");
            }
        }
    }
}
