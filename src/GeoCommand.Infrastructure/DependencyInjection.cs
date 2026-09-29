using GeoCommand.Application.Abstractions;
using GeoCommand.Infrastructure.DataSources;
using GeoCommand.Infrastructure.DataSources.Plugins;
using GeoCommand.Infrastructure.Persistence;
using GeoCommand.Sdk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace GeoCommand.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "GeoCommand";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // Bağlantı dizesi çözümlemesi DbContext ilk kez oluşturulurken yapılır; testler yapılandırmayı sonradan ezebilir.
        services.AddDbContext<GeoCommandDbContext>((sp, options) =>
        {
            var connectionString = sp.GetRequiredService<IConfiguration>().GetConnectionString(ConnectionStringName);
            if (string.IsNullOrWhiteSpace(connectionString))
                throw new InvalidOperationException(
                    $"'ConnectionStrings:{ConnectionStringName}' tanımlı değil. Parolayı repoya koymadan ayarlamak için " +
                    "'dotnet user-secrets' veya 'ConnectionStrings__GeoCommand' ortam değişkenini kullanın (README > Kurulum).");
            ConfigureDbContext(options, connectionString);
        });
        services.AddScoped<IGeoCommandDbContext>(sp => sp.GetRequiredService<GeoCommandDbContext>());

        services.AddOptions<PluginOptions>().Bind(configuration.GetSection(PluginOptions.Section));

        // Tüm kaynaklar aynı IPositionSource arayüzünü uygular. Yerleşik ikisi DI'dan, diğerleri plugin klasöründen
        // MEF ile gelir; hangisinin çalışacağına yapılandırma (açılışta) veya operatör (API'den) karar verir.
        services.AddSingleton<SimulatedPositionSource>();
        services.AddSingleton<FilePositionSource>();
        services.AddSingleton<IPositionSourceHost, PositionSourceHost>();
        services.AddSingleton(sp =>
        {
            var directory = sp.GetRequiredService<IOptions<PluginOptions>>().Value.Directory;
            if (!string.IsNullOrWhiteSpace(directory) && !Path.IsPathRooted(directory))
                directory = Path.GetFullPath(Path.Combine(sp.GetRequiredService<IHostEnvironment>().ContentRootPath, directory));

            PositionSourceEntry BuiltIn<T>(string name, string description) where T : IPositionSource =>
                new(name, description, PositionSourceOrigin.BuiltIn, null, null, () => sp.GetRequiredService<T>());

            return PositionSourceCatalog.Create(
                [
                    BuiltIn<SimulatedPositionSource>(SimulatedPositionSource.TypeName, "JSON senaryolarından tekrar üretilebilir simülasyon"),
                    BuiltIn<FilePositionSource>(FilePositionSource.TypeName, "CSV kayıt dosyası oynatıcısı")
                ],
                directory,
                sp.GetRequiredService<IPositionSourceHost>(),
                sp.GetRequiredService<ILoggerFactory>().CreateLogger<PositionSourceCatalog>());
        });

        services.AddOptions<DataSourceOptions>()
            .Bind(configuration.GetSection(DataSourceOptions.Section))
            .ValidateOnStart();
        // Seçilen tür katalogda olmalı; plugin'ler ancak çalışma anında bilindiği için doğrulama kataloğa bakar.
        services.AddSingleton<IValidateOptions<DataSourceOptions>, DataSourceOptionsValidator>();

        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention();
}
