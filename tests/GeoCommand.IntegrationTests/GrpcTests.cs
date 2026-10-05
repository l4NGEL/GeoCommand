using System.Net;
using GeoCommand.Api.GrpcServices;
using GeoCommand.Contracts;
using GeoCommand.Grpc.V1;
using GeoCommand.Infrastructure.Persistence;
using GeoCommand.IntegrationTests.Infrastructure;
using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using Proto = GeoCommand.Grpc.V1;

namespace GeoCommand.IntegrationTests;

/// <summary>
/// gRPC arayüzü gerçek PostGIS üzerinde: konum alma REST ile aynı hattan geçer (kaynak etiketi "grpc"), canlı yayın
/// filtrelenir ve anlık durumla başlar. Her test farklı bir araç ve coğrafi alan kullanır.
/// </summary>
public sealed class GrpcTests(GeoCommandApiFactory factory) : IClassFixture<GeoCommandApiFactory>
{
    private readonly HttpClient _http = factory.CreateClient();

    private static DateTimeOffset WholeSeconds(DateTimeOffset d) => new(d.Ticks - d.Ticks % TimeSpan.TicksPerSecond, TimeSpan.Zero);

    private static Guid IdOf(string callsign) => SeedData.Vehicles.Single(v => v.Callsign == callsign).Id;

    private static Proto.PositionReport Report(string callsign, double lat, double lon, DateTimeOffset? at) => new()
    {
        Callsign = callsign, Latitude = lat, Longitude = lon, SpeedMps = 12, HeadingDegrees = 45,
        Timestamp = at is { } t ? Timestamp.FromDateTimeOffset(t) : null
    };

    private async Task<PositionHistoryDto> HistoryAsync(string callsign, DateTimeOffset around)
    {
        var from = Uri.EscapeDataString(around.AddMinutes(-5).ToString("O"));
        var to = Uri.EscapeDataString(around.AddMinutes(5).ToString("O"));
        return await (await _http.GetAsync($"/api/vehicles/{IdOf(callsign)}/positions?from={from}&to={to}")).ReadAsync<PositionHistoryDto>();
    }

    [Fact]
    public async Task Unary_report_returns_the_vehicle_and_maps_rejections_to_grpc_status_codes()
    {
        var client = new Telemetry.TelemetryClient(factory.CreateGrpcChannel());
        var t0 = WholeSeconds(DateTimeOffset.UtcNow.AddMinutes(-1));

        var vehicle = await client.ReportPositionAsync(Report("bravo-2", 39.80, 32.70, t0));
        Assert.Equal(("BRAVO-2", IdOf("BRAVO-2").ToString()), (vehicle.Callsign, vehicle.Id));
        Assert.Equal(Proto.VehicleState.Moving, vehicle.State);
        Assert.Equal(39.80, vehicle.Latitude, 6);
        Assert.Equal(t0, vehicle.LastUpdate.ToDateTimeOffset());

        async Task<RpcException> Rejected(Proto.PositionReport report) =>
            await Assert.ThrowsAsync<RpcException>(async () => await client.ReportPositionAsync(report));

        var duplicate = await Rejected(Report("BRAVO-2", 39.80, 32.70, t0));
        Assert.Equal(StatusCode.AlreadyExists, duplicate.StatusCode);

        var unknown = await Rejected(Report("YOK-9", 39.80, 32.70, t0.AddSeconds(1)));
        Assert.Equal(StatusCode.NotFound, unknown.StatusCode);
        Assert.Contains("'YOK-9' çağrı adlı araç tanımlı değil.", unknown.Status.Detail); // Türkçe karakterler bozulmadan taşınır

        var invalid = await Rejected(Report("BRAVO-2", 95, 32.70, t0.AddSeconds(1)));
        Assert.Equal(StatusCode.InvalidArgument, invalid.StatusCode);

        var noTimestamp = await Rejected(Report("BRAVO-2", 39.80, 32.70, null));
        Assert.Equal(StatusCode.InvalidArgument, noTimestamp.StatusCode);
        Assert.Contains("timestamp", noTimestamp.Status.Detail);
    }

    [Fact]
    public async Task Client_stream_is_ingested_through_the_shared_pipeline_and_rejections_do_not_break_it()
    {
        var client = new Telemetry.TelemetryClient(factory.CreateGrpcChannel());
        var t0 = WholeSeconds(DateTimeOffset.UtcNow.AddMinutes(-2));

        using var call = client.StreamPositions();
        Proto.PositionReport[] reports =
        [
            Report("CHARLIE-3", 39.700, 32.600, t0),
            Report("CHARLIE-3", 39.701, 32.601, t0.AddSeconds(1)),
            Report("CHARLIE-3", 39.701, 32.601, t0.AddSeconds(1)), // yinelenen
            Report("YOK-9", 39.700, 32.600, t0),                   // tanımsız araç
            Report("CHARLIE-3", 39.702, 32.602, null),             // zaman damgası yok
            Report("CHARLIE-3", 39.702, 200, t0.AddSeconds(2)),    // geçersiz boylam
            Report("CHARLIE-3", 39.703, 32.603, t0.AddSeconds(3))  // hatalardan sonra akış sürer
        ];
        foreach (var report in reports) await call.RequestStream.WriteAsync(report);
        await call.RequestStream.CompleteAsync();
        var summary = await call;

        Assert.Equal((7L, 3L, 2L, 1L, 1L, 0L),
            (summary.Received, summary.Accepted, summary.Invalid, summary.UnknownVehicle, summary.Duplicate, summary.Conflict));
        Assert.Equal(
            [(2L, RejectionReason.Duplicate), (3L, RejectionReason.UnknownVehicle), (4L, RejectionReason.Invalid), (5L, RejectionReason.Invalid)],
            summary.Rejections.Select(r => (r.Index, r.Reason)));
        Assert.Contains(summary.Rejections[3].Errors, e => e.Contains("Boylam"));

        var history = await HistoryAsync("CHARLIE-3", t0);
        Assert.Equal([39.700, 39.701, 39.703], history.Points.Select(p => Math.Round(p.Latitude, 3)));
        Assert.All(history.Points, p => Assert.Equal("grpc", p.Source));
    }

    [Fact]
    public async Task Subscription_starts_with_a_filtered_snapshot_then_streams_live_updates_and_zone_events()
    {
        var zone = await (await _http.PostJsonAsync("/api/zones", new CreateZoneRequest("gRPC Test Bölgesi",
        [
            new(39.600, 32.500), new(39.600, 32.510), new(39.610, 32.510), new(39.610, 32.500)
        ]))).ReadAsync<ZoneDto>();
        var t0 = WholeSeconds(DateTimeOffset.UtcNow.AddMinutes(-3));
        // Abonelikten önceki son durum anlık durumda görünmeli.
        Assert.Equal(HttpStatusCode.Accepted,
            (await _http.PostJsonAsync("/api/positions", new PositionReportRequest("DELTA-4", 39.590, 32.505, 5, 0, t0))).StatusCode);

        var feed = new OperationsFeed.OperationsFeedClient(factory.CreateGrpcChannel());
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        using var call = feed.Subscribe(new SubscribeRequest { Callsigns = { " delta-4 " }, IncludeSnapshot = true }, cancellationToken: cancel.Token);
        var stream = call.ResponseStream;

        Assert.True(await stream.MoveNext(cancel.Token));
        var snapshot = stream.Current;
        Assert.True(snapshot.Snapshot);
        Assert.Equal(("DELTA-4", t0), (snapshot.Vehicle.Callsign, snapshot.Vehicle.LastUpdate.ToDateTimeOffset()));

        // Filtre dışındaki araç yayınlanmaz; DELTA-4 bölgeye girer.
        Assert.Equal(HttpStatusCode.Accepted,
            (await _http.PostJsonAsync("/api/positions", new PositionReportRequest("ALFA-1", 39.605, 32.505, 5, 0, t0.AddSeconds(1)))).StatusCode);
        var grpc = new Telemetry.TelemetryClient(factory.CreateGrpcChannel());
        await grpc.ReportPositionAsync(Report("DELTA-4", 39.605, 32.505, t0.AddSeconds(2)));

        Assert.True(await stream.MoveNext(cancel.Token));
        var moved = stream.Current;
        Assert.False(moved.Snapshot);
        Assert.Equal(("DELTA-4", t0.AddSeconds(2)), (moved.Vehicle.Callsign, moved.Vehicle.LastUpdate.ToDateTimeOffset()));

        Assert.True(await stream.MoveNext(cancel.Token));
        var entered = stream.Current.Event;
        Assert.Equal((Proto.GeoEventType.ZoneEntered, "DELTA-4", zone.Id.ToString()), (entered.Type, entered.Callsign, entered.ZoneId));
        Assert.Equal(39.605, entered.Latitude, 6);

        await cancel.CancelAsync();
        var ended = await Assert.ThrowsAsync<RpcException>(() => stream.MoveNext(CancellationToken.None));
        Assert.Equal(StatusCode.Cancelled, ended.StatusCode);
    }
}

/// <summary>Yayıncının abone tamponu davranışı; veritabanı gerektirmez.</summary>
public sealed class OperationsFeedBroadcasterTests
{
    private static VehicleDto Vehicle(string callsign) =>
        new(Guid.NewGuid(), callsign, Contracts.VehicleState.Moving, 39.9, 32.8, 5, 90, DateTimeOffset.UtcNow, null);

    [Fact]
    public async Task Slow_subscriber_is_disconnected_with_an_error_instead_of_silently_losing_updates()
    {
        var feed = new OperationsFeedBroadcaster(bufferSize: 2);
        using var slow = feed.Subscribe();
        using var fast = feed.Subscribe();

        feed.Publish(Vehicle("A"));
        feed.Publish(Vehicle("B"));
        Assert.True(fast.Reader.TryRead(out _));
        Assert.True(fast.Reader.TryRead(out _));
        feed.Publish(Vehicle("C")); // slow'un tamponu dolu

        Assert.Equal(1, feed.SubscriberCount);
        Assert.True(fast.Reader.TryRead(out var c));
        Assert.Equal("C", c!.Vehicle!.Callsign);

        // Tamponda kalanlar okunur, ardından taşma hatası gelir (OperationsFeedService bunu RESOURCE_EXHAUSTED'a çevirir).
        var received = new List<string>();
        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await foreach (var item in slow.Reader.ReadAllAsync()) received.Add(item.Vehicle!.Callsign);
        });
        Assert.Equal(["A", "B"], received);
        Assert.True(ex is SubscriberOverflowException || ex.InnerException is SubscriberOverflowException, ex.ToString());
    }

    [Fact]
    public async Task Fan_out_reaches_grpc_subscribers_even_when_signalr_broadcast_fails()
    {
        var feed = new OperationsFeedBroadcaster(bufferSize: 8);
        using var subscription = feed.Subscribe();
        var client = new FanOutOperationsClient(new FailingSignalR(), feed);

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.VehicleUpdated(Vehicle("A")));

        Assert.True(subscription.Reader.TryRead(out var item));
        Assert.Equal("A", item!.Vehicle!.Callsign);
    }

    [Fact]
    public void Disposed_subscription_stops_receiving()
    {
        var feed = new OperationsFeedBroadcaster(bufferSize: 8);
        var subscription = feed.Subscribe();
        subscription.Dispose();

        feed.Publish(Vehicle("A"));

        Assert.Equal(0, feed.SubscriberCount);
        Assert.False(subscription.Reader.TryRead(out _));
        Assert.True(subscription.Reader.Completion.IsCompletedSuccessfully);
    }

    private sealed class FailingSignalR : IOperationsClient
    {
        private static Task Fail() => Task.FromException(new InvalidOperationException("SignalR yayını başarısız."));
        public Task VehicleUpdated(VehicleDto vehicle) => Fail();
        public Task EventRaised(GeoEventDto geoEvent) => Fail();
        public Task MissionChanged(MissionDto mission) => Fail();
        public Task ZoneCreated(ZoneDto zone) => Fail();
        public Task ZoneDeleted(Guid zoneId) => Fail();
        public Task SourceStatusChanged(SourceStatusDto status) => Fail();
    }
}
