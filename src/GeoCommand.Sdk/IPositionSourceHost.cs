using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace GeoCommand.Sdk;

/// <summary>
/// API'nin plugin'lere sunduğu hizmetler. Plugin, kurucusunda <c>[ImportingConstructor]</c> ile bunu alır;
/// API'nin DI kapsayıcısına, veritabanına veya diğer iç türlerine erişmez.
/// </summary>
public interface IPositionSourceHost
{
    /// <summary><c>DataSource:&lt;sourceName&gt;</c> yapılandırma bölümü (ör. <c>DataSource:Nmea</c>).</summary>
    IConfigurationSection GetSettings(string sourceName);

    ILoggerFactory LoggerFactory { get; }

    /// <summary>Testlerde sahte saat verilebilsin diye doğrudan <see cref="TimeProvider.System"/> yerine kullanılır.</summary>
    TimeProvider Time { get; }
}
