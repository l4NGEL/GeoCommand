using System.Reflection;
using System.Runtime.Loader;

namespace GeoCommand.Infrastructure.DataSources.Plugins;

/// <summary>
/// Her plugin kendi yükleme bağlamında çalışır: plugin'in kendi bağımlılıkları (ör. farklı sürüm bir NuGet paketi)
/// API'ninkilerle çakışmaz ve <c>.deps.json</c> üzerinden plugin klasöründen çözülür.
/// Sözleşme derlemeleri ise bilinçli olarak paylaşılır; aksi hâlde plugin'deki <c>IPositionSource</c>, API'deki
/// <c>IPositionSource</c> ile aynı tür sayılmaz ve MEF hiçbir dışa aktarımı eşleştiremez.
/// </summary>
internal sealed class PluginLoadContext(string mainAssemblyPath)
    : AssemblyLoadContext(Path.GetFileNameWithoutExtension(mainAssemblyPath), isCollectible: false)
{
    private readonly AssemblyDependencyResolver _resolver = new(mainAssemblyPath);

    internal static bool IsShared(AssemblyName name) =>
        name.Name is { } n && (
            n == "GeoCommand.Sdk" ||
            n.StartsWith("System.Composition", StringComparison.Ordinal) ||
            n.StartsWith("Microsoft.Extensions.", StringComparison.Ordinal));

    protected override Assembly? Load(AssemblyName assemblyName)
    {
        // null: varsayılan bağlama (API'nin yüklediği kopyaya) bırak.
        if (IsShared(assemblyName)) return null;
        var path = _resolver.ResolveAssemblyToPath(assemblyName);
        return path is null ? null : LoadFromAssemblyPath(path);
    }

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {
        var path = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return path is null ? IntPtr.Zero : LoadUnmanagedDllFromPath(path);
    }
}
