using System.Collections.Concurrent;

namespace GeoCommand.Application.Abstractions;

/// <summary>
/// Aynı araç üzerinde çalışan işlemleri (konum alma, görev atama, çevrimdışı işaretleme) sıraya sokar.
/// Böylece bir işlemin yazdığı araç durumu diğerininkinin üzerine yazılmaz ve bölge geçişleri iki kez
/// değerlendirilmez. Tek API örneği için tasarlanmıştır (bkz. README - bilinen sınırlamalar).
/// </summary>
public sealed class VehicleLocks
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.Ordinal);

    public async Task<IDisposable> AcquireAsync(string callsign, CancellationToken cancellationToken)
    {
        var semaphore = _locks.GetOrAdd(callsign, _ => new SemaphoreSlim(1, 1));
        await semaphore.WaitAsync(cancellationToken);
        return new Releaser(semaphore);
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0) semaphore.Release();
        }
    }
}
