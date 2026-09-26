namespace GeoCommand.Desktop;

public sealed class MapOptions
{
    public bool UseOnlineTiles { get; set; } = true;
    public string UserAgent { get; set; } = "GeoCommand-PortfolioDemo/1.0";
    public double CenterLatitude { get; set; } = 39.915;
    public double CenterLongitude { get; set; } = 32.845;
    public double Resolution { get; set; } = 20;
}
