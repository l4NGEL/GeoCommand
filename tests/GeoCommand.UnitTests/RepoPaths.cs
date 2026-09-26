namespace GeoCommand.UnitTests;

internal static class RepoPaths
{
    public static string Root { get; } = FindRoot();

    public static string DataDirectory => Path.Combine(Root, "data");

    private static string FindRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "GeoCommand.slnx"))) return dir.FullName;
        throw new InvalidOperationException("Depo kökü (GeoCommand.slnx) bulunamadı.");
    }
}
