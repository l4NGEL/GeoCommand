using System.Text.Json.Serialization;
using GeoCommand.Api.Endpoints;
using GeoCommand.Api.GrpcServices;
using GeoCommand.Api.Hosting;
using GeoCommand.Api.Hubs;
using GeoCommand.Application;
using GeoCommand.Contracts;
using GeoCommand.Infrastructure;
using GeoCommand.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using Serilog;

// Başlangıç hataları için geçici logger; host kurulunca yapılandırmadaki logger ile değiştirilir.
// (CreateBootstrapLogger yerine düz logger: entegrasyon testleri aynı süreçte birden çok host açar.)
Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    builder.Services
        .AddApplication(builder.Configuration)
        .AddInfrastructure(builder.Configuration);

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<ApiExceptionHandler>();
    // Bozuk JSON / geçersiz parametre her ortamda ApiExceptionHandler'a düşsün (varsayılan: yalnızca Development).
    builder.Services.Configure<RouteHandlerOptions>(o => o.ThrowOnBadRequest = true);
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddOpenApi();
    builder.Services.AddHealthChecks().AddDbContextCheck<GeoCommandDbContext>("database");

    builder.Services.AddSignalR()
        .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    // gRPC: sahadan konum alma (Telemetry) ve dış sistemlere canlı yayın (OperationsFeed). Kestrel'de HTTP/2'ye ayrılmış
    // ayrı bir uç noktadan sunulur (appsettings.json > Kestrel:Endpoints:Grpc).
    builder.Services.AddGrpc(o => o.EnableDetailedErrors = builder.Environment.IsDevelopment());
    builder.Services.AddGrpcReflection();
    builder.Services.AddOptions<GrpcOptions>()
        .Bind(builder.Configuration.GetSection(GrpcOptions.Section))
        .Validate(o => o.SubscriberBufferSize is >= 1 and <= 100_000, "Grpc:SubscriberBufferSize 1-100000 arasında olmalı.")
        .ValidateOnStart();
    builder.Services.AddSingleton(sp => new OperationsFeedBroadcaster(sp.GetRequiredService<IOptions<GrpcOptions>>().Value.SubscriberBufferSize));

    // Application katmanı yayınları IOperationsClient üzerinden yapar; burada SignalR'a ve gRPC abonelerine bağlanır.
    builder.Services.AddSingleton<IOperationsClient>(sp => new FanOutOperationsClient(
        sp.GetRequiredService<IHubContext<OperationsHub, IOperationsClient>>().Clients.All,
        sp.GetRequiredService<OperationsFeedBroadcaster>()));

    builder.Services.AddSingleton<PositionSourceRunner>();
    builder.Services.AddHostedService(sp => sp.GetRequiredService<PositionSourceRunner>());
    builder.Services.AddHostedService<LivenessMonitor>();

    var app = builder.Build();

    app.UseExceptionHandler();
    app.UseStatusCodePages();
    app.UseSerilogRequestLogging(o =>
        o.GetLevel = (ctx, _, ex) => ex is not null || ctx.Response.StatusCode >= 500
            ? Serilog.Events.LogEventLevel.Error
            : ctx.Request.Path.StartsWithSegments("/api/positions") ? Serilog.Events.LogEventLevel.Debug : Serilog.Events.LogEventLevel.Information);

    if (app.Configuration.GetValue("Database:MigrateOnStartup", false))
        await app.Services.MigrateDatabaseAsync();

    if (app.Environment.IsDevelopment()) app.MapOpenApi();

    app.MapHealthChecks("/health");
    app.MapHub<OperationsHub>(HubRoutes.Operations);
    app.MapGeoCommandEndpoints();
    app.MapGrpcService<TelemetryService>();
    app.MapGrpcService<OperationsFeedService>();
    if (app.Environment.IsDevelopment()) app.MapGrpcReflectionService(); // grpcurl vb. araçlar için şema keşfi

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "API başlatılamadı: {Message}", ex.Message);
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program;
