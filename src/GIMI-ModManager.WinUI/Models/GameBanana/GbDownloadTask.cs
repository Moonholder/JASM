using CommunityToolkit.Mvvm.ComponentModel;
using GIMI_ModManager.Core.Services.GameBanana.Models;
using Microsoft.UI.Xaml;

namespace GIMI_ModManager.WinUI.Models.GameBanana;

public partial class GbDownloadTask : ObservableObject
{
    public static event Action? ActiveTasksChanged;

    public GbModDisplayItem Mod { get; init; } = null!;
    public ModFileInfo FileInfo { get; init; } = null!;
    public string? CategoryName { get; init; }
    public string? ModUrl { get; set; }
    public CancellationTokenSource? Cts { get; set; }
    public string? ArchivePath { get; set; }

    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _isError;

    public Visibility CancelVisibility => !IsCompleted && !IsError ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RemoveVisibility => IsCompleted || IsError ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RetryVisibility => IsCompleted || IsError ? Visibility.Visible : Visibility.Collapsed;

    partial void OnIsCompletedChanged(bool value)
    {
        OnPropertyChanged(nameof(CancelVisibility));
        OnPropertyChanged(nameof(RemoveVisibility));
        OnPropertyChanged(nameof(RetryVisibility));
        ActiveTasksChanged?.Invoke();
    }

    partial void OnIsErrorChanged(bool value)
    {
        OnPropertyChanged(nameof(CancelVisibility));
        OnPropertyChanged(nameof(RemoveVisibility));
        OnPropertyChanged(nameof(RetryVisibility));
        ActiveTasksChanged?.Invoke();
    }
}