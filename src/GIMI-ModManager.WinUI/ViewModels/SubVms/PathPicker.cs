using CommunityToolkit.Mvvm.ComponentModel;
using FluentValidation;
using Microsoft.UI;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.Storage.Pickers;
using System.Collections.ObjectModel;

namespace GIMI_ModManager.WinUI.ViewModels.SubVms;

public partial class PathPicker : ObservableRecipient
{
    [ObservableProperty] private string? _path = null;

    public bool PathHasValue => !string.IsNullOrWhiteSpace(Path);

    [ObservableProperty] private bool _isValid;

    public readonly ObservableCollection<InfoMessage> ValidationMessages = new();

    private readonly List<AbstractValidator<PathPicker>> _validators = new();

    public ReadOnlyCollection<AbstractValidator<PathPicker>> Validators => _validators.AsReadOnly();

    public event EventHandler? IsValidChanged;

    public string CommitButtonText = "选择";

    public PickerLocationId SuggestedStartLocation = PickerLocationId.ComputerFolder;

    public IList<string> FileTypeFilter = ["*"];

    private void RaiseIsValidChanged()
    {
        IsValidChanged?.Invoke(this, EventArgs.Empty);
    }

    public PathPicker(params AbstractValidator<PathPicker>[] validators)
    {
        _validators.AddRange(validators);
    }

    public PathPicker(IEnumerable<AbstractValidator<PathPicker>> validators)
    {
        _validators.AddRange(validators);
    }

    public void SetValidators(IEnumerable<AbstractValidator<PathPicker>> validators)
    {
        _validators.Clear();
        _validators.AddRange(validators);
        Validate();
    }

    public void Validate(string? pathToSett = null)
    {
        if (pathToSett is not null)
            Path = pathToSett;

        if (Path is null || string.IsNullOrWhiteSpace(pathToSett))
        {
            ValidationMessages.Clear();
            return;
        }

        ValidationMessages.Clear();
        foreach (var validator in _validators)
        {
            var result = validator.Validate(this);
            if (!result.IsValid)
            {
                result.Errors.ForEach(error =>
                {
                    var severity = error.Severity switch
                    {
                        Severity.Warning => InfoBarSeverity.Warning,
                        Severity.Info => InfoBarSeverity.Informational,
                        _ => InfoBarSeverity.Error
                    };
                    ValidationMessages.Add(new InfoMessage(error.ErrorMessage, severity));
                });
            }
        }

        var oldIsValid = IsValid;
        IsValid = ValidationMessages.All(message => message.Severity != InfoBarSeverity.Error);
        if (oldIsValid != IsValid)
            RaiseIsValidChanged();
    }

    public async Task BrowseFolderPathAsync(WindowEx window)
    {
        var windowId = GetWindowId(window);

        var folderPicker = new FolderPicker(windowId)
        {
            SuggestedStartLocation = SuggestedStartLocation,
            // CommitButtonText = CommitButtonText
        };

        var folder = await folderPicker.PickSingleFolderAsync();
        Path = folder?.Path;
    }

    public async Task BrowseFilePathAsync(WindowEx window)
    {
        var windowId = GetWindowId(window);

        var filePicker = new FileOpenPicker(windowId)
        {
            SuggestedStartLocation = SuggestedStartLocation,
            // CommitButtonText = CommitButtonText
        };
        foreach (var filter in FileTypeFilter)
        {
            filePicker.FileTypeFilter.Add(filter);
        }

        var file = await filePicker.PickSingleFileAsync();
        Path = file?.Path;
    }

    public async Task<string?> BrowseSaveFilePathAsync(WindowEx window, string defaultFileName = "")
    {
        var windowId = GetWindowId(window);

        var filePicker = new FileSavePicker(windowId)
        {
            SuggestedStartLocation = SuggestedStartLocation,
            // CommitButtonText = CommitButtonText
        };

        filePicker.FileTypeChoices.Add("Files", FileTypeFilter);

        if (!string.IsNullOrEmpty(defaultFileName))
        {
            filePicker.SuggestedFileName = defaultFileName;
        }

        var file = await filePicker.PickSaveFileAsync();
        return file?.Path;
    }

    // 辅助方法：从 WindowEx 获取 WindowId
    private static WindowId GetWindowId(WindowEx window)
    {
        // 获取窗口句柄
        var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(window);

        // 从窗口句柄获取 WindowId
        return Win32Interop.GetWindowIdFromWindow(hwnd);
    }
}

public readonly struct InfoMessage
{
    public InfoMessage(string message, InfoBarSeverity severity = InfoBarSeverity.Informational)
    {
        Message = message ?? string.Empty;
        Severity = severity;
    }

    public string Message { get; }
    public InfoBarSeverity Severity { get; }
}