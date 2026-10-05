using GeoCommand.Application.Ingestion;
using Grpc.Core;
using Proto = GeoCommand.Grpc.V1;

namespace GeoCommand.Api.GrpcServices;

/// <summary>
/// gRPC konum alma. REST ve veri kaynaklarıyla aynı <see cref="PositionIngestionService"/>'i kullanır.
/// Her bildirim kendi DI kapsamında işlenir: uzun süren bir akışta DbContext'in izlediği nesneler birikmez.
/// </summary>
public sealed class TelemetryService(IServiceScopeFactory scopes, ILogger<TelemetryService> logger) : Proto.Telemetry.TelemetryBase
{
    /// <summary>Özetteki ayrıntılı ret kaydı sınırı; sayaçlar sınırsızdır.</summary>
    public const int MaxRejectionDetails = 50;

    private const string MissingTimestamp = "Zaman damgası (timestamp) zorunlu.";

    public override async Task<Proto.Vehicle> ReportPosition(Proto.PositionReport request, ServerCallContext context)
    {
        var result = await IngestAsync(request, context.CancellationToken);
        if (result.Outcome == IngestOutcome.Accepted) return result.Vehicle!.ToMessage();

        var code = result.Outcome switch
        {
            IngestOutcome.Invalid => StatusCode.InvalidArgument,
            IngestOutcome.UnknownVehicle => StatusCode.NotFound,
            IngestOutcome.Duplicate => StatusCode.AlreadyExists,
            _ => StatusCode.Aborted
        };
        throw new RpcException(new Status(code, string.Join(" ", result.Errors)));
    }

    public override async Task<Proto.StreamSummary> StreamPositions(
        IAsyncStreamReader<Proto.PositionReport> requestStream, ServerCallContext context)
    {
        var summary = new Proto.StreamSummary();
        var ct = context.CancellationToken;
        logger.LogInformation("gRPC konum akışı açıldı. İstemci={Peer}", context.Peer);

        await foreach (var message in requestStream.ReadAllAsync(ct))
        {
            var index = summary.Received++;
            var result = await IngestAsync(message, ct);
            var reason = result.Outcome switch
            {
                IngestOutcome.Accepted => Proto.RejectionReason.Unspecified,
                IngestOutcome.Invalid => Proto.RejectionReason.Invalid,
                IngestOutcome.UnknownVehicle => Proto.RejectionReason.UnknownVehicle,
                IngestOutcome.Duplicate => Proto.RejectionReason.Duplicate,
                _ => Proto.RejectionReason.Conflict
            };
            switch (reason)
            {
                case Proto.RejectionReason.Unspecified: summary.Accepted++; continue;
                case Proto.RejectionReason.Invalid: summary.Invalid++; break;
                case Proto.RejectionReason.UnknownVehicle: summary.UnknownVehicle++; break;
                case Proto.RejectionReason.Duplicate: summary.Duplicate++; break;
                default: summary.Conflict++; break;
            }
            if (summary.Rejections.Count < MaxRejectionDetails)
                summary.Rejections.Add(new Proto.Rejection { Index = index, Callsign = message.Callsign, Reason = reason, Errors = { result.Errors } });
        }

        logger.LogInformation(
            "gRPC konum akışı kapandı. İstemci={Peer} Alınan={Received} Kabul={Accepted} Geçersiz={Invalid} BilinmeyenAraç={Unknown} Yinelenen={Duplicate} Çakışma={Conflict}",
            context.Peer, summary.Received, summary.Accepted, summary.Invalid, summary.UnknownVehicle, summary.Duplicate, summary.Conflict);
        return summary;
    }

    private async Task<IngestResult> IngestAsync(Proto.PositionReport message, CancellationToken ct)
    {
        if (message.ToReport() is not { } report) return IngestResult.Rejected(IngestOutcome.Invalid, MissingTimestamp);

        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<PositionIngestionService>().IngestAsync(report, ct);
    }
}
