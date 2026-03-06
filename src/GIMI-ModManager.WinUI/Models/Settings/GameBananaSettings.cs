using Newtonsoft.Json;

namespace GIMI_ModManager.WinUI.Models.Settings;

public class GameBananaSettings
{
    [JsonIgnore] public const string Key = "GameBananaSettings";
    [JsonIgnore] public const string DownloadHistoryKey = "GbDownloadHistory";

    public string SelectedSort { get; set; } = "default";
    public int NsfwPolicy { get; set; } = 1; // 0=Remove, 1=Blur, 2=Show
    public int ModelFilter { get; set; } = 1; // 0=All, 1=ModsOnly
}

public class DownloadHistoryEntry
{
    public string ModId { get; set; } = string.Empty;
    public string CategoryName { get; set; } = string.Empty;
    public string ModUrl { get; set; } = string.Empty;
    public string ModName { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string FileId { get; set; } = string.Empty;
    public string StatusMessage { get; set; } = string.Empty;
    public string ArchivePath { get; set; } = string.Empty;
    public bool IsError { get; set; }
    public bool IsCompleted { get; set; }
    public double ProgressPercentage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.Now;
}