using GeoCommand.Sdk;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GeoCommand.Infrastructure.DataSources.Plugins;

/// <summary>Plugin'lere MEF üzerinden verilen dar hizmet yüzeyi.</summary>
public sealed class PositionSourceHost(IConfiguration configuration, ILoggerFactory loggerFactory, TimeProvider time) : IPositionSourceHost
{
    public IConfigurationSection GetSettings(string sourceName) =>
        configuration.GetSection($"{DataSourceOptions.Section}:{sourceName}");

    public ILoggerFactory LoggerFactory => loggerFactory;

    public TimeProvider Time => time;
}
