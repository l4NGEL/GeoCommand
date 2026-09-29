using GeoCommand.Application.Ingestion;
using GeoCommand.Contracts;
using GeoCommand.Domain.Common;
using GeoCommand.Infrastructure.DataSources;
using GeoCommand.Infrastructure.DataSources.Plugins;
using GeoCommand.Sdk;
using Microsoft.Extensions.Options;

namespace GeoCommand.Api.Hosting;

/// <summary>
/// Seçili <see cref="IPositionSource"/>'u (yerleşik veya plugin) arka planda çalıştırır ve her bildirimi HTTP ile
/// gelenlerle aynı alma hattından geçirir. Açılıştaki kaynak <c>DataSource:Type</c>'tan gelir; operatör API üzerinden
/// başka bir kaynağa geçebilir, başlatıp durdurabilir. Aynı anda tek kaynak çalışır.
/// </summary>
public sealed class PositionSourceRunner(
    PositionSourceCatalog catalog,
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
    private PositionSourceEntry? _selected;

    private PositionSourceEntry Selected =>
        _selected ??= catalog.Find(options.Value.Type)
                      ?? throw new InvalidOperationException($"DataSource:Type '{options.Value.Type}' katalogda yok.");

    public SourceStatusDto Status
    {
        get
        {
            var source = Selected.Source;
            return new(source.SourceType, _loop is { IsCompleted: false }, _activeScenario, source.AvailableScenarios, _lastError);
        }
    }

    public IReadOnlyList<SourceTypeDto> SourceTypes =>
        catalog.Entries.Select(e =>
        {
            // Bir plugin'in senaryo listesi hata verirse diğer kaynaklar yine listelenebilmeli.
            try
            {
                return new SourceTypeDto(e.Name, e.Description, e.Origin.ToString(), e.Version, e.Source.AvailableScenarios, e.Source.DefaultScenario);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Kaynak bilgisi okunamadı. Kaynak={Source}", e.Name);
                return new SourceTypeDto(e.Name, $"{e.Description} (hata: {ex.Message})", e.Origin.ToString(), e.Version, [], "");
            }
        }).ToList();

    async Task IHostedService.StartAsync(CancellationToken cancellationToken)
    {
        if (!options.Value.AutoStart) return;
        try
        {
            await StartSourceAsync(null, null, cancellationToken);
        }
        catch (Exception ex)
        {
            // Kaynak başlatılamasa da API çalışmaya devam eder; hata durum uç noktasında görünür.
            _lastError = ex.Message;
            logger.LogError(ex, "Veri kaynağı otomatik başlatılamadı.");
        }
    }

    async Task IHostedService.StopAsync(CancellationToken cancellationToken) => await StopSourceAsync(cancellationToken);

    public async Task<SourceStatusDto> StartSourceAsync(string? sourceType, string? scenario, CancellationToken cancellationToken)
    {
        var entry = string.IsNullOrWhiteSpace(sourceType)
            ? Selected
            : catalog.Find(sourceType.Trim()) ?? throw new DomainValidationException([
                $"'{sourceType.Trim()}' adlı veri kaynağı yok. Mevcut: {string.Join(", ", catalog.Entries.Select(e => e.Name))}."]);
        var source = entry.Source;

        scenario = string.IsNullOrWhiteSpace(scenario) ? source.DefaultScenario : scenario.Trim();
        var match = source.AvailableScenarios.FirstOrDefault(s => string.Equals(s, scenario, StringComparison.OrdinalIgnoreCase))
                    ?? throw new DomainValidationException([
                        $"'{scenario}' {source.SourceType} kaynağında bulunamadı. Mevcut: {string.Join(", ", source.AvailableScenarios)}."]);

        await _gate.WaitAsync(cancellationToken);
        try
        {
            await StopCoreAsync();
            _selected = entry;
            _cts = new CancellationTokenSource();
            _activeScenario = match;
            _lastError = null;
            var token = _cts.Token;
            _loop = Task.Run(() => RunAsync(source, match, token), CancellationToken.None);
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

    private async Task RunAsync(IPositionSource source, string scenario, CancellationToken cancellationToken)
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
