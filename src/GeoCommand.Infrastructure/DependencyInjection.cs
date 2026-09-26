using GeoCommand.Application.Abstractions;
using GeoCommand.Application.Ingestion;
using GeoCommand.Infrastructure.DataSources;
using GeoCommand.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

        services.AddOptions<DataSourceOptions>()
            .Bind(configuration.GetSection(DataSourceOptions.Section))
            .Validate(o => o.Type is SimulatedPositionSource.TypeName or FilePositionSource.TypeName,
                $"DataSource:Type '{SimulatedPositionSource.TypeName}' veya '{FilePositionSource.TypeName}' olmalı.")
            .ValidateOnStart();

        // İki kaynak da aynı IPositionSource arayüzünü uygular; hangisinin kullanılacağına yapılandırma karar verir.
        services.AddSingleton<SimulatedPositionSource>();
        services.AddSingleton<FilePositionSource>();
        services.AddSingleton<IPositionSource>(sp =>
        {
            var type = sp.GetRequiredService<IOptions<DataSourceOptions>>().Value.Type;
            return string.Equals(type, FilePositionSource.TypeName, StringComparison.OrdinalIgnoreCase)
                ? sp.GetRequiredService<FilePositionSource>()
                : sp.GetRequiredService<SimulatedPositionSource>();
        });

        return services;
    }

    internal static void ConfigureDbContext(DbContextOptionsBuilder options, string connectionString) =>
        options
            .UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite())
            .UseSnakeCaseNamingConvention();
}
