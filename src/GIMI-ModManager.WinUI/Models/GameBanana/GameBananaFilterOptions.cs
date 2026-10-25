namespace GIMI_ModManager.WinUI.Models.GameBanana;

public record SortOption(string DisplayName, string Value);

public record FilterOption<T>(string DisplayName, T Value);