using GeoCommand.Api.Hosting;
using GeoCommand.Application.Ingestion;
using GeoCommand.Sdk;
using GeoCommand.Application.Missions;
using GeoCommand.Application.Queries;
using GeoCommand.Application.Zones;
using GeoCommand.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace GeoCommand.Api.Endpoints;

public static class GeoCommandEndpoints
{
    public const string HttpReportSource = "http";

    public static IEndpointRouteBuilder MapGeoCommandEndpoints(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        var vehicles = api.MapGroup("/vehicles").WithTags("Araçlar");
        vehicles.MapGet("/", (OperationsQueries q, CancellationToken ct) => q.GetVehiclesAsync(ct));
        vehicles.MapGet("/{id:guid}", (Guid id, OperationsQueries q, CancellationToken ct) => q.GetVehicleAsync(id, ct));
        vehicles.MapGet("/{id:guid}/positions",
            (Guid id, DateTimeOffset from, DateTimeOffset to, int? limit, OperationsQueries q, CancellationToken ct) =>
                q.GetPositionHistoryAsync(id, from, to, limit, ct));
        vehicles.MapGet("/{id:guid}/missions", (Guid id, MissionService s, CancellationToken ct) => s.GetForVehicleAsync(id, ct));
        vehicles.MapPost("/{id:guid}/missions",
            async (Guid id, AssignMissionRequest? request, MissionService s, CancellationToken ct) =>
            {
                var mission = await s.AssignAsync(id, request, ct);
                return TypedResults.Created($"/api/missions/{mission.Id}", mission);
            });

        api.MapPut("/missions/{id:guid}/status",
                (Guid id, ChangeMissionStatusRequest? request, MissionService s, CancellationToken ct) => s.ChangeStatusAsync(id, request, ct))
            .WithTags("Görevler");

        api.MapGet("/events",
                (Guid? vehicleId, DateTimeOffset? from, DateTimeOffset? to, int? limit, OperationsQueries q, CancellationToken ct) =>
                    q.GetEventsAsync(vehicleId, from, to, limit, ct))
            .WithTags("Olaylar");

        var zones = api.MapGroup("/zones").WithTags("Bölgeler");
        zones.MapGet("/", (ZoneService s, CancellationToken ct) => s.GetZonesAsync(ct));
        zones.MapPost("/", async (CreateZoneRequest? request, ZoneService s, CancellationToken ct) =>
        {
            var zone = await s.CreateAsync(request, ct);
            return TypedResults.Created($"/api/zones/{zone.Id}", zone);
        });
        zones.MapDelete("/{id:guid}", async (Guid id, ZoneService s, CancellationToken ct) =>
        {
            await s.DeleteAsync(id, ct);
            return TypedResults.NoContent();
        });
        zones.MapGet("/{id:guid}/vehicles", (Guid id, ZoneService s, CancellationToken ct) => s.GetVehiclesInZoneAsync(id, ct));

        api.MapPost("/positions", IngestAsync).WithTags("Konum bildirimi");

        var source = api.MapGroup("/source").WithTags("Veri kaynağı");
        source.MapGet("/", (PositionSourceRunner r) => r.Status);
        source.MapGet("/types", (PositionSourceRunner r) => r.SourceTypes);
        source.MapPost("/start", (StartSourceRequest? request, PositionSourceRunner r, CancellationToken ct) =>
            r.StartSourceAsync(request?.SourceType, request?.Scenario, ct));
        source.MapPost("/stop", (PositionSourceRunner r, CancellationToken ct) => r.StopSourceAsync(ct));

        return app;
    }

    /// <summary>Harici bir kaynaktan HTTP ile gelen bildirim; simülatörle aynı doğrulama ve olay hattından geçer.</summary>
    private static async Task<Results<Accepted<VehicleDto>, ProblemHttpResult>> IngestAsync(
        PositionReportRequest? request, PositionIngestionService ingestion, CancellationToken ct)
    {
        if (request is null)
            return TypedResults.Problem(ApiExceptionHandler.Validation(["İstek gövdesi boş olamaz."]));

        var report = new PositionReport(request.Callsign, request.Latitude, request.Longitude,
            request.SpeedMps, request.HeadingDegrees, request.Timestamp, HttpReportSource);
        var result = await ingestion.IngestAsync(report, ct);

        return result.Outcome switch
        {
            IngestOutcome.Accepted => TypedResults.Accepted($"/api/vehicles/{result.Vehicle!.Id}", result.Vehicle),
            IngestOutcome.Invalid => TypedResults.Problem(ApiExceptionHandler.Validation(result.Errors)),
            IngestOutcome.UnknownVehicle => TypedResults.Problem(new ProblemDetails
            {
                Status = StatusCodes.Status404NotFound, Title = "Araç bulunamadı.", Detail = result.Errors[0]
            }),
            _ => TypedResults.Problem(new ProblemDetails
            {
                Status = StatusCodes.Status409Conflict, Title = "Bildirim uygulanmadı.", Detail = result.Errors[0],
                Extensions = { ["outcome"] = result.Outcome.ToString() }
            })
        };
    }
}
