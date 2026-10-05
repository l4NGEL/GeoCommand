using System.Collections.Concurrent;
using System.Threading.Channels;
using GeoCommand.Contracts;

namespace GeoCommand.Api.GrpcServices;

public sealed class GrpcOptions
{
    public const string Section = "Grpc";

    /// <summary>Abone başına bekleyen güncelleme sınırı. Dolarsa abonelik RESOURCE_EXHAUSTED ile kapatılır.</summary>
    public int SubscriberBufferSize { get; set; } = 1024;
}

/// <summary>Bir abonenin tamponu doldu: istemci yayına yetişemiyor.</summary>
public sealed class SubscriberOverflowException(int capacity)
    : Exception($"Abone tamponu doldu ({capacity} güncelleme); istemci yayına yetişemiyor.");

/// <summary>
/// gRPC <c>OperationsFeed</c> abonelerine canlı güncellemeleri dağıtır. Yayın, alma hattını hiçbir zaman bekletmez:
/// her abonenin kendi sınırlı tamponu vardır ve yavaş bir abone yalnızca kendi aboneliğini kaybeder. Mesaj sessizce
/// atlanmaz; tampon dolunca abonelik hatayla kapanır ve istemci anlık durumla yeniden abone olur.
/// </summary>
public sealed class OperationsFeedBroadcaster(int bufferSize)
{
    private readonly ConcurrentDictionary<Subscription, byte> _subscribers = new();

    public int SubscriberCount => _subscribers.Count;

    public Subscription Subscribe()
    {
        var subscription = new Subscription(this, bufferSize);
        _subscribers.TryAdd(subscription, 0);
        return subscription;
    }

    public void Publish(VehicleDto vehicle) => Publish(new FeedItem(vehicle, null));

    public void Publish(GeoEventDto geoEvent) => Publish(new FeedItem(null, geoEvent));

    private void Publish(FeedItem item)
    {
        foreach (var subscription in _subscribers.Keys) subscription.Offer(item);
    }

    /// <summary>Yayındaki tek bir öğe: ya araç durumu ya olay.</summary>
    public sealed record FeedItem(VehicleDto? Vehicle, GeoEventDto? Event);

    public sealed class Subscription : IDisposable
    {
        private readonly OperationsFeedBroadcaster _owner;
        private readonly int _capacity;
        private readonly Channel<FeedItem> _channel;

        internal Subscription(OperationsFeedBroadcaster owner, int capacity)
        {
            _owner = owner;
            _capacity = capacity;
            _channel = Channel.CreateBounded<FeedItem>(new BoundedChannelOptions(capacity)
            {
                SingleReader = true,
                FullMode = BoundedChannelFullMode.Wait // TryWrite dolu kanalda false döner; bekleme yapılmaz
            });
        }

        public ChannelReader<FeedItem> Reader => _channel.Reader;

        internal void Offer(FeedItem item)
        {
            if (_channel.Writer.TryWrite(item)) return;
            // Tamamlanmış kanala yazma da false döner; hata yalnızca ilk taşmada konur.
            if (_channel.Writer.TryComplete(new SubscriberOverflowException(_capacity)))
                _owner._subscribers.TryRemove(this, out _);
        }

        public void Dispose()
        {
            _owner._subscribers.TryRemove(this, out _);
            _channel.Writer.TryComplete();
        }
    }
}

/// <summary>
/// Uygulama katmanının yayınlarını hem masaüstü istemcilere (SignalR) hem gRPC abonelerine iletir.
/// gRPC yayını önce yapılır: bellek içidir, hata vermez ve SignalR yayınındaki bir hatadan etkilenmez.
/// </summary>
public sealed class FanOutOperationsClient(IOperationsClient signalR, OperationsFeedBroadcaster feed) : IOperationsClient
{
    public Task VehicleUpdated(VehicleDto vehicle)
    {
        feed.Publish(vehicle);
        return signalR.VehicleUpdated(vehicle);
    }

    public Task EventRaised(GeoEventDto geoEvent)
    {
        feed.Publish(geoEvent);
        return signalR.EventRaised(geoEvent);
    }

    public Task MissionChanged(MissionDto mission) => signalR.MissionChanged(mission);
    public Task ZoneCreated(ZoneDto zone) => signalR.ZoneCreated(zone);
    public Task ZoneDeleted(Guid zoneId) => signalR.ZoneDeleted(zoneId);
    public Task SourceStatusChanged(SourceStatusDto status) => signalR.SourceStatusChanged(status);
}
