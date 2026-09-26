using System.Text.Json.Serialization;
using GeoCommand.Api.Endpoints;
using GeoCommand.Api.Hosting;
using GeoCommand.Api.Hubs;
using GeoCommand.Application;
using GeoCommand.Contracts;
using GeoCommand.Infrastructure;
using GeoCommand.Infrastructure.Persistence;
using Microsoft.AspNetCore.SignalR;
using Serilog;

Log.Logger = new LoggerConfiguration().WriteTo.Console().CreateBootstrapLogger();

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
    builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddOpenApi();
    builder.Services.AddHealthChecks().AddDbContextCheck<GeoCommandDbContext>("database");

    builder.Services.AddSignalR()
        .AddJsonProtocol(o => o.PayloadSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    // Application katmanı yayınları IOperationsClient üzerinden yapar; burada SignalR'a bağlanır.
    builder.Services.AddSingleton<IOperationsClient>(sp =>
        sp.GetRequiredService<IHubContext<OperationsHub, IOperationsClient>>().Clients.All);

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
