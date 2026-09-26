using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace GeoCommand.Infrastructure.Persistence;

/// <summary>
/// <c>dotnet ef migrations add</c> için. Migration üretmek veritabanına bağlanmayı gerektirmez;
/// <c>dotnet ef database update</c> için GEOCOMMAND_CONNECTION ortam değişkeni ayarlanmalıdır.
/// </summary>
internal sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<GeoCommandDbContext>
{
    public GeoCommandDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("GEOCOMMAND_CONNECTION")
                               ?? "Host=localhost;Port=5433;Database=geocommand;Username=geocommand";
        var options = new DbContextOptionsBuilder<GeoCommandDbContext>();
        DependencyInjection.ConfigureDbContext(options, connectionString);
        return new GeoCommandDbContext(options.Options);
    }
}
