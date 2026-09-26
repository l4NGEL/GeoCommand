using Microsoft.Extensions.Logging;

namespace GeoCommand.Application.Abstractions;

internal static class Broadcast
{
    /// <summary>
    /// Veri kaydedildikten sonra yayın yapılır; yayın hatası kaydı geri almaz. İstemciler yeniden
    /// bağlandıklarında güncel durumu REST üzerinden çektikleri için kaçan bir mesaj kalıcı tutarsızlık yaratmaz.
    /// </summary>
    public static async Task SafeAsync(ILogger logger, Func<Task> send)
    {
        try
        {
            await send();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SignalR yayını başarısız oldu.");
        }
    }
}
