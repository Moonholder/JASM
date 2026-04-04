using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities;
using GIMI_ModManager.Core.Helpers;
using Serilog;
using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.RegularExpressions;

namespace GIMI_ModManager.Core.Services;

public partial class UserPreferencesService(ILogger logger, ISkinManagerService skinManagerService)
{
    private readonly ILogger _logger = logger.ForContext<UserPreferencesService>();
    private readonly ISkinManagerService _skinManagerService = skinManagerService;

    private DirectoryInfo _threeMigotoFolder = null!;
    private DirectoryInfo _activeModsFolder = null!;
    private static string D3DX_USER_INI = Constants.UserIniFileName;

    [GeneratedRegex(@"^\s*(?<keywords>(?:global\s+|persist\s+)*)\$?(?<key>\w+)\s*=\s*(?<value>.*?)\s*(?<comment>(?:;|//).*)?$", RegexOptions.IgnoreCase)]
    private static partial Regex GetVariableRegex();

    [GeneratedRegex(@"^\s*namespace\s*=\s*(?<ns>[^;\s\n\r]+)", RegexOptions.IgnoreCase)]
    private static partial Regex GetNamespaceRegex();

    public Task InitializeAsync()
    {
        _threeMigotoFolder = new DirectoryInfo(_skinManagerService.ThreeMigotoRootfolder);
        _activeModsFolder = new DirectoryInfo(_skinManagerService.ActiveModsFolderPath);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Saves the mod preferences to the mod settings file
    /// This overrides the existing preferences in the mod settings file
    /// 3Dmigoto should do a refresh (F10) so that it store the new preferences in the d3dx_user.ini
    /// And we save the mod preferences to the mod settings files
    /// Returns  True if success, returns false if 3MigotoFolder or d3dxUserIni is not found or d3dxUserIni is invalid
    /// </summary>
    public async Task<bool> SaveModPreferencesAsync(Guid? modId = null)
    {
        if (!_threeMigotoFolder.Exists) return false;

        var d3dxUserIni = new FileInfo(Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI));
        if (!d3dxUserIni.Exists) return false;

        var lines = await File.ReadAllLinesAsync(d3dxUserIni.FullName).ConfigureAwait(false);
        var activeMods = _skinManagerService.GetAllMods(GetOptions.Enabled).AsEnumerable();

        if (modId is not null && modId != Guid.Empty)
            activeMods = activeMods.Where(ske => ske.Mod.Id == modId);

        foreach (var characterSkinEntry in activeMods)
        {
            var modSettings = await characterSkinEntry.Mod.Settings.TryReadSettingsAsync(false).ConfigureAwait(false);
            if (modSettings is null) continue;

            var existingModPrefs = FindExistingModPref(_activeModsFolder.FullName, lines, characterSkinEntry);

            var prefDict = existingModPrefs
                .Where(x => x.HasKeyValue)
                .ToDictionary(
                    x => x.FullKeyPath,
                    x => x.KeyValuePair!.Value.Value,
                    StringComparer.OrdinalIgnoreCase
                );

            modSettings.SetPreferences(prefDict);
            await characterSkinEntry.Mod.Settings.SaveSettingsAsync(modSettings).ConfigureAwait(false);
        }
        return true;
    }

    /// <summary>
    /// Overrides the mod preferences in the d3dx_user.ini file with the mod settings preferences
    /// Returns  True if success, returns false if 3MigotoFolder or d3dxUserIni is not found or d3dxUserIni is invalid
    /// </summary>
    public async Task<bool> SetModPreferencesAsync(Guid? modId = null, CancellationToken cancellationToken = default)
    {
        if (!_threeMigotoFolder.Exists) return false;

        var d3dxUserIni = new FileInfo(Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI));
        if (!d3dxUserIni.Exists) return false;

        var lines = (await File.ReadAllLinesAsync(d3dxUserIni.FullName, cancellationToken).ConfigureAwait(false)).ToList();
        var constantSectionIndex = lines.FindIndex(x => IniConfigHelpers.IsSection(x, "Constants"));
        if (constantSectionIndex == -1) return false;

        var activeMods = _skinManagerService.GetAllMods(GetOptions.Enabled)
            .Where(ske => !ske.Mod.Settings.TryGetSettings(out var modSettings) || modSettings.Preferences.Any());

        if (modId is not null && modId != Guid.Empty)
            activeMods = activeMods.Where(ske => ske.Mod.Id == modId);

        foreach (var characterSkinEntry in activeMods)
        {
            var modSettings = await characterSkinEntry.Mod.Settings.TryReadSettingsAsync(false, cancellationToken).ConfigureAwait(false);
            if (modSettings is null || !modSettings.Preferences.Any()) continue;

            var existing = FindExistingModPref(_activeModsFolder.FullName, lines.ToArray(), characterSkinEntry);
            int insertIndex = existing.Any() ? existing.Min(x => x.Index) : constantSectionIndex + 1;
            foreach (var pref in existing.OrderByDescending(x => x.Index)) lines.RemoveAt(pref.Index);

            foreach (var kv in modSettings.Preferences)
            {
                lines.Insert(insertIndex, $"{kv.Key} = {kv.Value}");
            }
        }

        await File.WriteAllLinesAsync(d3dxUserIni.FullName, lines, cancellationToken).ConfigureAwait(false);
        return true;
    }

    /// <summary>
    /// Reads the current values from d3dx_user.ini for a specific mod,
    /// and updates the initialization values in the mod's local .ini files (under [Constants]).
    /// </summary>
    public async Task<bool> SyncPreferencesToModLocalFilesAsync(Guid guid)
    {
        var skinEntry = _skinManagerService.GetModEntryById(guid);
        if (skinEntry is null || !_threeMigotoFolder.Exists) return false;

        var d3dxUserIniPath = Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI);
        if (!File.Exists(d3dxUserIniPath)) return false;

        var d3dxLines = await File.ReadAllLinesAsync(d3dxUserIniPath).ConfigureAwait(false);
        var globalLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in d3dxLines)
        {
            var trimmed = line.Trim();
            if (!trimmed.StartsWith('$')) continue;
            var parts = trimmed.Split('=', 2);
            if (parts.Length == 2) globalLookup[parts[0].Trim()] = parts[1].Trim();
        }

        var modFolderPath = skinEntry.Mod.FullPath;
        var validIniFiles = Directory.GetFiles(modFolderPath, "*.ini", SearchOption.AllDirectories)
            .Where(f => !IsPathDisabled(f, modFolderPath));

        bool anyFileUpdated = false;
        string modRootPrefix = CreateModRootPrefix(_activeModsFolder.FullName).ToLower();
        string modBasePath = GetModRelativeBasePath(skinEntry);

        foreach (var iniFile in validIniFiles)
        {
            var encoding = GetEncoding(iniFile);
            var fileLines = await File.ReadAllLinesAsync(iniFile, encoding).ConfigureAwait(false);
            string? fileNamespace = GetNamespaceFromLines(fileLines);

            string prefix = !string.IsNullOrEmpty(fileNamespace)
                ? $"$\\{fileNamespace.ToLower()}\\"
                : $"{modRootPrefix}{modBasePath}\\{Path.GetRelativePath(modFolderPath, iniFile).Replace('/', '\\').ToLower()}\\";

            var (updatedContent, isModified) = ProcessIniLines(fileLines, prefix, globalLookup);

            if (isModified)
            {
                await File.WriteAllLinesAsync(iniFile, updatedContent, encoding).ConfigureAwait(false);
                anyFileUpdated = true;
                _logger.Information("Synced {File} (Namespace: {Ns})", Path.GetFileName(iniFile), fileNamespace ?? "None");
            }
        }

        return anyFileUpdated;
    }

    private (List<string> lines, bool modified) ProcessIniLines(string[] lines, string prefix, Dictionary<string, string> lookup)
    {
        var result = new List<string>();
        bool isModified = false;
        bool inConstants = false;

        foreach (var line in lines)
        {
            var trimLine = line.Trim();
            if (trimLine.StartsWith('[') && trimLine.EndsWith(']'))
            {
                inConstants = trimLine.Equals("[Constants]", StringComparison.OrdinalIgnoreCase);
                result.Add(line);
                continue;
            }

            if (inConstants)
            {
                var match = GetVariableRegex().Match(line);
                if (match.Success)
                {
                    var varName = match.Groups["key"].Value;
                    var fullKey = prefix + varName.ToLower();

                    if (lookup.TryGetValue(fullKey, out var newValue))
                    {
                        var currentValue = match.Groups["value"].Value.Trim();
                        if (!currentValue.Equals(newValue, StringComparison.OrdinalIgnoreCase))
                        {
                            var indent = new string(line.TakeWhile(char.IsWhiteSpace).ToArray());
                            var keywords = match.Groups["keywords"].Value;
                            var hasDollar = line.Contains($"${varName}");
                            var comment = match.Groups["comment"].Value;

                            var newLine = $"{indent}{keywords}{(hasDollar ? "$" : "")}{varName} = {newValue}";
                            if (!string.IsNullOrEmpty(comment)) newLine += $" {comment}";

                            result.Add(newLine);
                            isModified = true;
                            continue;
                        }
                    }
                }
            }
            result.Add(line);
        }
        return (result, isModified);
    }

    private string GetModRelativeBasePath(CharacterSkinEntry skinEntry)
    {
        var category = skinEntry.ModList.Character.ModCategory.InternalName.ToString().ToLower();
        var character = skinEntry.ModList.Character.InternalName.ToString().ToLower();
        var modName = skinEntry.Mod.Name;
        if (modName.StartsWith(ModFolderHelpers.DISABLED_PREFIX))
            modName = modName[ModFolderHelpers.DISABLED_PREFIX.Length..];

        return Path.Combine(category, character, modName.ToLower()).Replace('/', '\\');
    }

    private bool IsPathDisabled(string fullPath, string rootPath)
    {
        var relative = Path.GetRelativePath(rootPath, fullPath);
        return relative.Split(Path.DirectorySeparatorChar).Any(p => p.StartsWith(ModFolderHelpers.ALT_DISABLED_PREFIX, StringComparison.OrdinalIgnoreCase));
    }

    private Encoding GetEncoding(string filename)
    {
        using var reader = new StreamReader(filename, Encoding.Default, true);
        reader.Peek();
        return reader.CurrentEncoding;
    }

    private string? GetNamespaceFromLines(IEnumerable<string> lines)
    {
        foreach (var line in lines)
        {
            var trimmed = line.Trim();
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith(';') || trimmed.StartsWith("//")) continue;
            if (trimmed.StartsWith('[')) break;
            var match = GetNamespaceRegex().Match(trimmed);
            if (match.Success) return match.Groups["ns"].Value;
        }
        return null;
    }

    private string CreateModRootPrefix(string rootModFolderPath)
    {
        return "$" + Path.DirectorySeparatorChar + rootModFolderPath.TrimEnd(Path.DirectorySeparatorChar).Split(Path.DirectorySeparatorChar).Last() + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// 查找该 Mod 对应的所有 Namespace 前缀以及物理路径前缀。
    /// </summary>
    private List<IniPreference> FindExistingModPref(string rootModFolderPath, string[] lines, CharacterSkinEntry skinEntry)
    {
        var result = new List<IniPreference>();
        var modRootPrefix = CreateModRootPrefix(rootModFolderPath).ToLower();
        var modBasePath = GetModRelativeBasePath(skinEntry);

        var pathPrefix = $"{modRootPrefix}{modBasePath}\\".ToLower();
        var namespaces = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // 扫描 Mod 文件夹下所有可能的 namespace
        if (Directory.Exists(skinEntry.Mod.FullPath))
        {
            var iniFiles = Directory.GetFiles(skinEntry.Mod.FullPath, "*.ini", SearchOption.AllDirectories)
                .Where(f => !IsPathDisabled(f, skinEntry.Mod.FullPath));

            foreach (var file in iniFiles)
            {
                var ns = GetNamespaceFromLines(File.ReadLines(file));
                if (!string.IsNullOrEmpty(ns)) namespaces.Add(ns.ToLower());
            }
        }

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();
            if (!line.StartsWith('$')) continue;

            string? matchedPrefix = null;
            // 匹配物理路径前缀
            if (line.StartsWith(pathPrefix)) matchedPrefix = pathPrefix;
            else
            {
                // 匹配任何一个在该 Mod 中定义的 Namespace 前缀
                foreach (var ns in namespaces)
                {
                    var nsPrefix = $"$\\{ns}\\";
                    if (line.StartsWith(nsPrefix)) { matchedPrefix = nsPrefix; break; }
                }
            }

            if (matchedPrefix != null)
            {
                var parts = line.Split('=', 2);
                if (parts.Length == 2)
                {
                    // 存储 FullKeyPath (例如 "$\mymod\var")
                    result.Add(new IniPreference(parts[0].Trim(), parts[0].Trim(), parts[1].Trim()) { Index = i });
                }
            }
        }
        return result;
    }

    public async Task Clear3DMigotoModPreferencesAsync(bool resetOnlyEnabledMods)
    {
        var getOption = resetOnlyEnabledMods ? GetOptions.Enabled : GetOptions.All;
        var mods = _skinManagerService.GetAllMods(getOption);
        if (!_threeMigotoFolder.Exists) return;
        var d3dxUserIni = Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI);
        if (!File.Exists(d3dxUserIni)) return;

        var lines = (await File.ReadAllLinesAsync(d3dxUserIni).ConfigureAwait(false)).ToList();
        foreach (var characterSkinEntry in mods)
        {
            var existingModPrefs = FindExistingModPref(_activeModsFolder.FullName, lines.ToArray(), characterSkinEntry);
            foreach (var pref in existingModPrefs.OrderByDescending(x => x.Index)) lines.RemoveAt(pref.Index);
        }
        await File.WriteAllLinesAsync(d3dxUserIni, lines).ConfigureAwait(false);
    }

    public async Task ResetPreferencesAsync(bool resetOnlyEnabledMods)
    {
        var activeMods = _skinManagerService.GetAllMods(resetOnlyEnabledMods ? GetOptions.Enabled : GetOptions.All);
        await Parallel.ForEachAsync(activeMods, async (ske, ct) =>
        {
            var settings = await ske.Mod.Settings.TryReadSettingsAsync(false, ct).ConfigureAwait(false);
            if (settings != null)
            {
                settings.SetPreferences(null);
                await ske.Mod.Settings.SaveSettingsAsync(settings).ConfigureAwait(false);
            }
        });
    }

    internal class IniPreference(string fullPath, string key, string value)
    {
        public int Index { get; set; } = -1;
        public string FullKeyPath { get; } = fullPath;
        public KeyValuePair<string, string>? KeyValuePair = new(key, value);

        [MemberNotNullWhen(true, nameof(KeyValuePair))]
        public bool HasKeyValue => KeyValuePair is not null;
    }
}