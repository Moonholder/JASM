using System.Collections.ObjectModel;

namespace GIMI_ModManager.WinUI.Models.Settings;

public class CommonPasswordOptions
{
    public const string Key = "CommonPasswordOptions";

    public ObservableCollection<PasswordEntry>? PasswordEntries { get; set; }

    public string? LastSelectedPassword { get; set; }
}