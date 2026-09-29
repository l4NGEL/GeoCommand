using Microsoft.Extensions.Options;

namespace GeoCommand.Infrastructure.DataSources.Plugins;

/// <summary><c>DataSource:Type</c> yerleşik veya yüklenmiş bir plugin kaynağını göstermeli; aksi hâlde API açılmaz.</summary>
internal sealed class DataSourceOptionsValidator(PositionSourceCatalog catalog) : IValidateOptions<DataSourceOptions>
{
    public ValidateOptionsResult Validate(string? name, DataSourceOptions options)
    {
        if (catalog.Find(options.Type) is not null) return ValidateOptionsResult.Success;

        var message = $"DataSource:Type '{options.Type}' tanınmıyor. Mevcut kaynaklar: {string.Join(", ", catalog.Entries.Select(e => e.Name))}.";
        if (catalog.LoadErrors.Count > 0) message += $" Plugin hataları: {string.Join(" ", catalog.LoadErrors)}";
        return ValidateOptionsResult.Fail(message);
    }
}
