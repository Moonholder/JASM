using CommunityToolkit.Mvvm.ComponentModel;
using GIMI_ModManager.Core.Services.GameBanana.Models;
using Microsoft.UI.Xaml;

namespace GIMI_ModManager.WinUI.Models.GameBanana;

public partial class GbDownloadTask : ObservableObject
{
    public static event Action? ActiveTasksChanged;

    public GbModDisplayItem Mod { get; init; } = null!;
    public ModFileInfo FileInfo { get; set; } = null!;
    public string? CategoryName { get; init; }
    public string? ModUrl { get; set; }
    public string? AuthorName { get; set; }
    public CancellationTokenSource? Cts { get; set; }
    public string? ArchivePath { get; set; }
    public string? PreviewImageUrl { get; set; }

    [ObservableProperty] private double _progressPercentage;
    [ObservableProperty] private string _statusMessage = string.Empty;
    [ObservableProperty] private bool _isCompleted;
    [ObservableProperty] private bool _isError;
    [ObservableProperty] private bool _isPaused;
    [ObservableProperty] private bool _isInstalling;

    public Visibility PauseVisibility => !IsCompleted && !IsError && !IsPaused && !IsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility ResumeVisibility => !IsCompleted && !IsError && IsPaused && !IsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CancelVisibility => !IsCompleted && !IsError && !IsInstalling ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RemoveVisibility => IsCompleted || IsError ? Visibility.Visible : Visibility.Collapsed;
    public Visibility RetryVisibility => (IsCompleted || IsError) && !IsPaused ? Visibility.Visible : Visibility.Collapsed;

    private void NotifyVisibilityChanged()
    {
        void Notify()
        {
            OnPropertyChanged(nameof(PauseVisibility));
            OnPropertyChanged(nameof(ResumeVisibility));
            OnPropertyChanged(nameof(CancelVisibility));
            OnPropertyChanged(nameof(RemoveVisibility));
            OnPropertyChanged(nameof(RetryVisibility));
            ActiveTasksChanged?.Invoke();
        }

        if (App.MainWindow?.DispatcherQueue is { HasThreadAccess: false } dq)
        {
            dq.TryEnqueue(Notify);
        }
        else
        {
            Notify();
        }
    }

    partial void OnIsInstallingChanged(bool value) => NotifyVisibilityChanged();
    partial void OnIsPausedChanged(bool value) => NotifyVisibilityChanged();
    partial void OnIsCompletedChanged(bool value) => NotifyVisibilityChanged();
    partial void OnIsErrorChanged(bool value) => NotifyVisibilityChanged();
}