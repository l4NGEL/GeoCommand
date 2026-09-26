using System.Windows;
using System.Windows.Threading;
using GeoCommand.Desktop.Services;
using GeoCommand.Desktop.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Serilog;

namespace GeoCommand.Desktop;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            ContentRootPath = AppContext.BaseDirectory,
            Args = e.Args
        });
        builder.Configuration.AddEnvironmentVariables("GEOCOMMAND_");

        builder.Services.AddSerilog((_, logger) => logger
            .ReadFrom.Configuration(builder.Configuration)
            .Enrich.FromLogContext());

        builder.Services.Configure<MapOptions>(builder.Configuration.GetSection("Map"));

        var apiBaseUrl = new Uri(builder.Configuration["Api:BaseUrl"] ?? "http://localhost:5080");
        builder.Services.AddHttpClient<ApiClient>(c =>
        {
            c.BaseAddress = apiBaseUrl;
            c.Timeout = TimeSpan.FromSeconds(15);
        });
        builder.Services.AddSingleton(sp => new LiveConnection(apiBaseUrl, sp.GetRequiredService<ILogger<LiveConnection>>()));
        builder.Services.AddSingleton<MainViewModel>();
        builder.Services.AddSingleton<MainWindow>();

        _host = builder.Build();
        await _host.StartAsync();

        var logger = _host.Services.GetRequiredService<ILogger<App>>();
        logger.LogInformation("GeoCommand istemcisi başladı. API={ApiBaseUrl} Harita altlığı={Tiles}",
            apiBaseUrl, _host.Services.GetRequiredService<IOptions<MapOptions>>().Value.UseOnlineTiles);

        var window = _host.Services.GetRequiredService<MainWindow>();
        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.Services.GetRequiredService<LiveConnection>().DisposeAsync();
            await _host.StopAsync(TimeSpan.FromSeconds(2));
            _host.Dispose();
        }
        await Log.CloseAndFlushAsync();
        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "İşlenmeyen arayüz hatası.");
        MessageBox.Show($"Beklenmeyen bir hata oluştu:\n{e.Exception.Message}\n\nAyrıntılar log dosyasına yazıldı.",
            "GeoCommand", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
