namespace GIMI_ModManager.Core.Services.GameBanana.Models;

using GIMI_ModManager.Core.Services.GameBanana.ApiModels;
using System.Text.Json.Serialization;
using System.ComponentModel;

public partial class ModFileInfo : INotifyPropertyChanged
{
    public ModFileInfo(ApiModFileInfo apiModFileInfo, string modId)
    {
        ModId = modId;
        FileId = apiModFileInfo.FileId.ToString();
        FileName = apiModFileInfo.FileName;
        Description = apiModFileInfo.Description ?? string.Empty;
        DateAdded = DateTimeOffset.FromUnixTimeSeconds(apiModFileInfo.DateAdded).DateTime;
        Md5Checksum = apiModFileInfo.Md5Checksum;
        DownloadCount = apiModFileInfo.DownloadCount;
        FileSize = apiModFileInfo.FileSize;
        DownloadUrl = apiModFileInfo.DownloadUrl;
        Version = apiModFileInfo.Version;
        AvResult = apiModFileInfo.AvResult;
    }

    public ModFileInfo(string modId, string fileId, string fileName, string description, string md5Checksum,
        DateTime dateAdded)
    {
        ModId = modId;
        FileId = fileId;
        FileName = fileName;
        Description = description;
        Md5Checksum = md5Checksum;
        DateAdded = dateAdded;
    }

    [JsonConstructor]
    [Obsolete("This constructor is for serialization purposes only.")]
#pragma warning disable CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    public ModFileInfo()
#pragma warning restore CS8618 // Non-nullable field must contain a non-null value when exiting constructor. Consider declaring as nullable.
    {
    }

    public string ModId { get; init; }
    public string FileId { get; init; }
    public string FileName { get; init; }
    public string Description { get; init; }
    public DateTime DateAdded { get; init; }
    [JsonIgnore] public TimeSpan Age => DateTime.Now - DateAdded;
    public string Md5Checksum { get; init; }
    public int DownloadCount { get; init; }
    public long FileSize { get; init; }
    public string? DownloadUrl { get; init; }
    public string? Version { get; init; }
    public string? AvResult { get; init; }

    [JsonIgnore]
    public string FormattedFileSize
    {
        get
        {
            if (FileSize <= 0) return "";
            if (FileSize < 1024) return $"{FileSize} B";
            if (FileSize < 1024 * 1024) return $"{FileSize / 1024.0:F1} KB";
            return $"{FileSize / (1024.0 * 1024.0):F1} MB";
        }
    }

    [JsonIgnore]
    public string FormattedDownloadCount => DownloadCount >= 1000
        ? $"{DownloadCount / 1000.0:F1}k"
        : DownloadCount.ToString();

    [JsonIgnore]
    public string FormattedVersion
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Version)) return string.Empty;
            return Version.StartsWith("v", StringComparison.OrdinalIgnoreCase) ? Version : $"v{Version}";
        }
    }

    [JsonIgnore]
    public string AvStatusIcon => AvResult switch
    {
        "clean" => "\uE73E",  // Checkmark
        _ => string.Empty
    };

    private bool _isDownloading;
    [JsonIgnore]
    public bool IsDownloading
    {
        get => _isDownloading;
        set
        {
            if (_isDownloading != value)
            {
                _isDownloading = value;
                NotifyStateProperties();
            }
        }
    }

    private bool _isInstalling;
    [JsonIgnore]
    public bool IsInstalling
    {
        get => _isInstalling;
        set
        {
            if (_isInstalling != value)
            {
                _isInstalling = value;
                NotifyStateProperties();
            }
        }
    }

    private bool _isInstalled;
    [JsonIgnore]
    public bool IsInstalled
    {
        get => _isInstalled;
        set
        {
            if (_isInstalled != value)
            {
                _isInstalled = value;
                NotifyStateProperties();
            }
        }
    }

    [JsonIgnore]
    public bool IsActiveDownload => IsDownloading && !IsInstalling;

    [JsonIgnore]
    public bool IsIdle => !IsDownloading && !IsInstalling && !IsInstalled;

    private void NotifyStateProperties()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsDownloading)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInstalling)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsInstalled)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActiveDownload)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsIdle)));
    }

    private int _downloadProgress;
    [JsonIgnore]
    public int DownloadProgress
    {
        get => _downloadProgress;
        set
        {
            if (_downloadProgress != value)
            {
                _downloadProgress = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DownloadProgress)));
            }
        }
    }

    private bool _isPaused;
    [JsonIgnore]
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            if (_isPaused != value)
            {
                _isPaused = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPaused)));
            }
        }
    }

    private string _downloadStatus = string.Empty;
    [JsonIgnore]
    public string DownloadStatus
    {
        get => _downloadStatus;
        set
        {
            if (_downloadStatus != value)
            {
                _downloadStatus = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DownloadStatus)));
            }
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}