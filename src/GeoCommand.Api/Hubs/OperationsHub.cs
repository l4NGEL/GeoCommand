using GeoCommand.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace GeoCommand.Api.Hubs;

/// <summary>
/// Canlı yayın kanalı. İstemciden sunucuya komut almaz; komutlar REST uç noktalarından gelir,
/// böylece doğrulama ve hata yanıtları tek yerde kalır.
/// </summary>
public sealed class OperationsHub(ILogger<OperationsHub> logger) : Hub<IOperationsClient>
{
    public override Task OnConnectedAsync()
    {
        logger.LogInformation("İstemci bağlandı. Bağlantı={ConnectionId}", Context.ConnectionId);
        return base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        logger.LogInformation("İstemci ayrıldı. Bağlantı={ConnectionId} Neden={Reason}", Context.ConnectionId, exception?.Message ?? "normal");
        return base.OnDisconnectedAsync(exception);
    }
}
