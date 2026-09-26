using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using GeoCommand.Contracts;

namespace GeoCommand.Desktop.Converters;

public static class Palette
{
    public static Color ForStatus(VehicleState status) => status switch
    {
        VehicleState.Moving => Color.FromRgb(0x2E, 0x9E, 0x5B),
        VehicleState.Idle => Color.FromRgb(0xD4, 0x9A, 0x1F),
        VehicleState.OnMission => Color.FromRgb(0x2F, 0x6F, 0xD6),
        VehicleState.Offline => Color.FromRgb(0x8A, 0x8F, 0x98),
        _ => Color.FromRgb(0xB0, 0xB4, 0xBA)
    };

    public static Color ForEvent(GeoEventType type) => type switch
    {
        GeoEventType.ZoneEntered => Color.FromRgb(0x2E, 0x9E, 0x5B),
        GeoEventType.ZoneExited => Color.FromRgb(0xD9, 0x6C, 0x1E),
        GeoEventType.MissionAssigned or GeoEventType.MissionStatusChanged => Color.FromRgb(0x2F, 0x6F, 0xD6),
        GeoEventType.VehicleOffline => Color.FromRgb(0xC0, 0x39, 0x2B),
        GeoEventType.VehicleOnline => Color.FromRgb(0x16, 0x8A, 0x8A),
        _ => Colors.Gray
    };
}

public static class TurkishText
{
    public static string Of(object? value) => value switch
    {
        VehicleState.Unknown => "Bilinmiyor",
        VehicleState.Idle => "Beklemede",
        VehicleState.Moving => "Hareket halinde",
        VehicleState.OnMission => "Görevde",
        VehicleState.Offline => "Çevrimdışı",
        MissionState.Assigned => "Atandı",
        MissionState.InProgress => "Yürütülüyor",
        MissionState.Completed => "Tamamlandı",
        MissionState.Cancelled => "İptal edildi",
        MissionPriority.Low => "Düşük",
        MissionPriority.Normal => "Normal",
        MissionPriority.High => "Yüksek",
        MissionPriority.Critical => "Kritik",
        GeoEventType.ZoneEntered => "Bölgeye giriş",
        GeoEventType.ZoneExited => "Bölgeden çıkış",
        GeoEventType.MissionAssigned => "Görev atandı",
        GeoEventType.MissionStatusChanged => "Görev durumu",
        GeoEventType.VehicleOffline => "Çevrimdışı",
        GeoEventType.VehicleOnline => "Yeniden çevrimiçi",
        null => "",
        _ => value.ToString() ?? ""
    };
}

public sealed class TurkishTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => TurkishText.Of(value);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class StatusBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        VehicleState s => new SolidColorBrush(Palette.ForStatus(s)),
        GeoEventType e => new SolidColorBrush(Palette.ForEvent(e)),
        _ => Brushes.Gray
    };
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>UTC zamanı yerel saate çevirir. ConverterParameter biçim dizesidir (varsayılan HH:mm:ss).</summary>
public sealed class LocalTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        DateTimeOffset d => d.ToLocalTime().ToString(parameter as string ?? "HH:mm:ss", CultureInfo.GetCultureInfo("tr-TR")),
        _ => "—"
    };
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null || value is string { Length: 0 } ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolToVisibleConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        (value is true) ^ (parameter as string == "invert") ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class CoordinateConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? d.ToString("0.00000", CultureInfo.InvariantCulture) : "—";
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
