using GeoCommand.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace GeoCommand.Api.Hosting;

internal static class DatabaseMigration
{
    public static async Task MigrateDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<GeoCommandDbContext>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<GeoCommandDbContext>>();

        try
        {
            // MigrateAsync geçmiş tablosunu gerekirse kendisi oluşturur; önceden sorgulamak boş veritabanında hata logu üretir.
            await db.Database.MigrateAsync();
            var applied = (await db.Database.GetAppliedMigrationsAsync()).ToList();
            logger.LogInformation("Veritabanı güncel. Son migration={Migration} Toplam={Count}", applied.LastOrDefault(), applied.Count);

            // postgis uzantısı bu migration'da oluşturulduysa Npgsql'in geometry tipini tanıması için tipleri yenile.
            if (db.Database.GetDbConnection() is NpgsqlConnection connection)
            {
                await connection.OpenAsync();
                await connection.ReloadTypesAsync();
                await connection.CloseAsync();
            }
        }
        catch (NpgsqlException ex)
        {
            throw new InvalidOperationException(
                "Veritabanına bağlanılamadı. 'docker compose up -d' ile PostGIS'in çalıştığını ve bağlantı dizesindeki " +
                $"port/parolanın .env ile aynı olduğunu kontrol edin. Ayrıntı: {ex.Message}", ex);
        }
    }
}
