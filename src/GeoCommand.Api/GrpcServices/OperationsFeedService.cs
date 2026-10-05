using System.Threading.Channels;
using GeoCommand.Application.Queries;
using Grpc.Core;
using Proto = GeoCommand.Grpc.V1;

namespace GeoCommand.Api.GrpcServices;

/// <summary>
/// Dış sistemlere canlı araç durumu ve olay akışı. Abonelik anlık durum okunmadan <b>önce</b> açılır; böylece anlık
/// durum ile canlı akış arasında kaçan güncelleme olmaz. Tamponda bekleyen ve anlık durumdan eski kalan araç
/// güncellemeleri gönderilmez (istemcideki durum geriye gitmesin).
/// </summary>
public sealed class OperationsFeedService(
    OperationsFeedBroadcaster feed,
    IServiceScopeFactory scopes,
    IHostApplicationLifetime lifetime,
    ILogger<OperationsFeedService> logger) : Proto.OperationsFeed.OperationsFeedBase
{
    public override async Task Subscribe(Proto.SubscribeRequest request, IServerStreamWriter<Proto.OperationsUpdate> responseStream, ServerCallContext context)
    {
        // API kapanırken açık akışlar normal biçimde (OK) sonlanır; aksi hâlde kapanış istemcilerin ayrılmasını beklerdi.
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken, lifetime.ApplicationStopping);
        var ct = stop.Token;

        var callsigns = request.Callsigns.Select(c => c.Trim()).Where(c => c.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);
        bool Matches(string callsign) => callsigns.Count == 0 || callsigns.Contains(callsign);

        using var subscription = feed.Subscribe();
        logger.LogInformation("gRPC abonesi bağlandı. İstemci={Peer} Filtre={Callsigns} AnlıkDurum={Snapshot}",
            context.Peer, callsigns.Count == 0 ? "tümü" : string.Join(",", callsigns), request.IncludeSnapshot);

        var snapshotTimes = new Dictionary<Guid, DateTimeOffset>();
        long sent = 0;
        try
        {
            if (request.IncludeSnapshot)
            {
                await using var scope = scopes.CreateAsyncScope();
                var vehicles = await scope.ServiceProvider.GetRequiredService<OperationsQueries>().GetVehiclesAsync(ct);
                foreach (var vehicle in vehicles.Where(v => Matches(v.Callsign)))
                {
                    if (vehicle.LastUpdateUtc is { } at) snapshotTimes[vehicle.Id] = at;
                    await responseStream.WriteAsync(new Proto.OperationsUpdate { Vehicle = vehicle.ToMessage(), Snapshot = true }, ct);
                    sent++;
                }
            }

            await foreach (var item in subscription.Reader.ReadAllAsync(ct))
            {
                Proto.OperationsUpdate update;
                if (item.Vehicle is { } vehicle)
                {
                    if (!Matches(vehicle.Callsign)) continue;
                    // Eşit zaman damgası gönderilir: çevrimdışı geçişi konum zamanını değiştirmez.
                    if (snapshotTimes.TryGetValue(vehicle.Id, out var snapshotAt) && vehicle.LastUpdateUtc < snapshotAt) continue;
                    update = new Proto.OperationsUpdate { Vehicle = vehicle.ToMessage() };
                }
                else
                {
                    var geoEvent = item.Event!;
                    if (!Matches(geoEvent.Callsign)) continue;
                    update = new Proto.OperationsUpdate { Event = geoEvent.ToMessage() };
                }
                await responseStream.WriteAsync(update, ct);
                sent++;
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception ex) when (ex is SubscriberOverflowException || ex.InnerException is SubscriberOverflowException)
        {
            logger.LogWarning("gRPC abonesi yetişemediği için ayrıldı. İstemci={Peer} Gönderilen={Sent}", context.Peer, sent);
            throw new RpcException(new Status(StatusCode.ResourceExhausted,
                (ex as SubscriberOverflowException ?? ex.InnerException)!.Message + " include_snapshot ile yeniden abone olun."));
        }
        catch (ChannelClosedException)
        {
            // Abonelik kapatıldı (Dispose); normal sonlanma.
        }

        logger.LogInformation("gRPC abonesi ayrıldı. İstemci={Peer} Gönderilen={Sent}", context.Peer, sent);
    }
}
