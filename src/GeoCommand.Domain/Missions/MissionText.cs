namespace GeoCommand.Domain.Missions;

/// <summary>Olay mesajları ve hata metinleri için Türkçe adlar.</summary>
public static class MissionText
{
    public static string ToTurkish(this MissionPriority priority) => priority switch
    {
        MissionPriority.Low => "düşük",
        MissionPriority.Normal => "normal",
        MissionPriority.High => "yüksek",
        MissionPriority.Critical => "kritik",
        _ => priority.ToString()
    };

    public static string ToTurkish(this MissionStatus status) => status switch
    {
        MissionStatus.Assigned => "Atandı",
        MissionStatus.InProgress => "Yürütülüyor",
        MissionStatus.Completed => "Tamamlandı",
        MissionStatus.Cancelled => "İptal edildi",
        _ => status.ToString()
    };
}
