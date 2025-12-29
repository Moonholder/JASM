using GIMI_ModManager.Core.Contracts.Services;
using GIMI_ModManager.Core.Entities;
using GIMI_ModManager.Core.Helpers;
using Serilog;
using System.Diagnostics.CodeAnalysis;

namespace GIMI_ModManager.Core.Services;

public class UserPreferencesService(ILogger logger, ISkinManagerService skinManagerService)
{
    private readonly ILogger _logger = logger.ForContext<UserPreferencesService>();
    private readonly ISkinManagerService _skinManagerService = skinManagerService;

    private DirectoryInfo _threeMigotoFolder = null!;
    private DirectoryInfo _activeModsFolder = null!;
    private static string D3DX_USER_INI = Constants.UserIniFileName;


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
        if (!_threeMigotoFolder.Exists)
        {
            _logger.Warning("3DMigoto folder does not exist");
            return false;
        }

        var d3dxUserIni = new FileInfo(Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI));
        if (!d3dxUserIni.Exists)
        {
            _logger.Information("d3dx_user.ini does not exist in 3DMigoto folder");
            return false;
        }

        var lines = await File.ReadAllLinesAsync(d3dxUserIni.FullName).ConfigureAwait(false);

        var activeMods = _skinManagerService.GetAllMods(GetOptions.Enabled).AsEnumerable();

        if (modId is not null && modId != Guid.Empty)
            activeMods = activeMods.Where(ske => ske.Mod.Id == modId);

        foreach (var characterSkinEntry in activeMods)
        {
            var modSettings = await characterSkinEntry.Mod.Settings.TryReadSettingsAsync(false).ConfigureAwait(false);
            if (modSettings is null)
                continue;

            var existingModPref = FindExistingModPref(_activeModsFolder.FullName, lines, characterSkinEntry);


            var keyValues = existingModPref
                .Where(x => x.HasKeyValue || x.KeyValuePair is not null)
                .Select(x => x.KeyValuePair!.Value);

            var pref = new Dictionary<string, string>(keyValues);
            modSettings.SetPreferences(pref);

            await characterSkinEntry.Mod.Settings.SaveSettingsAsync(modSettings).ConfigureAwait(false);
        }

        return true;
    }


    public async Task Clear3DMigotoModPreferencesAsync(bool resetOnlyEnabledMods)
    {
        var getOption = resetOnlyEnabledMods ? GetOptions.Enabled : GetOptions.All;

        var mods = _skinManagerService.GetAllMods(getOption);

        if (!_threeMigotoFolder.Exists)
            throw new DirectoryNotFoundException($"3DMigoto folder not found at {_threeMigotoFolder.FullName}");

        var d3dxUserIni = new FileInfo(Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI));
        if (!d3dxUserIni.Exists)
        {
            _logger.Debug("d3dx_user.ini does not exist in 3DMigoto folder");
            return;
        }

        var lines = (await File.ReadAllLinesAsync(d3dxUserIni.FullName).ConfigureAwait(false)).ToList();

        foreach (var characterSkinEntry in mods)
        {
            var existingModPref = FindExistingModPref(_activeModsFolder.FullName, lines, characterSkinEntry);

            var reversedList = existingModPref.ToList();
            reversedList.Reverse();
            foreach (var pref in reversedList)
            {
                lines.RemoveAt(pref.Index);
            }
        }

        await File.WriteAllLinesAsync(d3dxUserIni.FullName, lines).ConfigureAwait(false);
        _logger.Information("3DMigoto mod preferences cleared for {ModTypes}", getOption.ToString());
    }

    /// <summary>
    /// Overrides the mod preferences in the d3dx_user.ini file with the mod settings preferences
    /// Returns  True if success, returns false if 3MigotoFolder or d3dxUserIni is not found or d3dxUserIni is invalid
    /// </summary>
    public async Task<bool> SetModPreferencesAsync(Guid? modId = null, CancellationToken cancellationToken = default)
    {
        if (!_threeMigotoFolder.Exists)
        {
            _logger.Warning("3DMigoto folder does not exist");
            return false;
        }


        var d3dxUserIni = new FileInfo(Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI));
        if (!d3dxUserIni.Exists)
        {
            _logger.Information("d3dx_user.ini does not exist in 3DMigoto folder");
            return false;
        }

        var lines =
            (await File.ReadAllLinesAsync(d3dxUserIni.FullName, cancellationToken).ConfigureAwait(false)).ToList();

        var constantSectionIndex =
            lines.IndexOf(lines.FirstOrDefault(x => IniConfigHelpers.IsSection(x, "Constants")) ?? "SomeString");

        if (constantSectionIndex == -1)
        {
            _logger.Warning("Constants section not found in d3dx_user.ini");
            return false;
        }


        var activeMods = _skinManagerService.GetAllMods(GetOptions.Enabled)
            .OrderBy(ske => ske.ModList.Character.InternalName.Id)
            .Where(ske => !ske.Mod.Settings.TryGetSettings(out var modSettings) || modSettings.Preferences.Any());

        if (modId is not null && modId != Guid.Empty)
            activeMods = activeMods.Where(ske => ske.Mod.Id == modId);


        foreach (var characterSkinEntry in activeMods)
        {
            var modSettings = await characterSkinEntry.Mod.Settings.TryReadSettingsAsync(false, cancellationToken)
                .ConfigureAwait(false);
            if (modSettings is null || !modSettings.Preferences.Any())
                continue;

            var explicitNamespace = TryGetExplicitNamespace(characterSkinEntry.Mod.FullPath);

            var modSettingsPref = modSettings.Preferences
                .Select(kv => CreateUserIniPreference(
                                    _activeModsFolder.FullName,
                                    characterSkinEntry,
                                    kv,
                                    explicitNamespace))
                .Where(pref => pref.HasKeyValue)
                .ToArray();

            var existingModPref = FindExistingModPref(_activeModsFolder.FullName, lines, characterSkinEntry);

            // Remove existing ones for this mode
            var reversedList = existingModPref.ToList();
            reversedList.Reverse();
            foreach (var pref in reversedList)
            {
                lines.RemoveAt(pref.Index);
            }

            // Add new ones from mod settings

            var i = existingModPref.FirstOrDefault()?.Index ?? constantSectionIndex + 2;
            foreach (var iniPreference in modSettingsPref)
            {
                lines.Insert(i, iniPreference);
            }
        }

        var rootModFolderPrefix = CreateModRootPrefix(_activeModsFolder.FullName);
        var lastModIndex = lines.FindLastIndex(
            x => x.StartsWith(rootModFolderPrefix, StringComparison.OrdinalIgnoreCase));

        if (lastModIndex != -1)
        {
            lines.Sort(constantSectionIndex + 1, lastModIndex - constantSectionIndex,
                StringComparer.OrdinalIgnoreCase);
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

        if (skinEntry is null)
        {
            _logger.Warning("Mod with ID {ModId} not found among active mods", guid);
            return false;
        }

        // 2. 读取 d3dx_user.ini 配置
        if (!_threeMigotoFolder.Exists) return false;
        var d3dxUserIniPath = Path.Combine(_threeMigotoFolder.FullName, D3DX_USER_INI);
        if (!File.Exists(d3dxUserIniPath)) return false;

        string? explicitNamespace = TryGetExplicitNamespace(skinEntry.Mod.FullPath);
        var d3dxLines = await File.ReadAllLinesAsync(d3dxUserIniPath).ConfigureAwait(false);

        var globalPreferences = FindExistingModPref(_activeModsFolder.FullName, d3dxLines, skinEntry)
            .Where(p => p.HasKeyValue)
            .ToDictionary(
                p => p.KeyValuePair!.Value.Key,
                p => p.KeyValuePair!.Value.Value,
                StringComparer.OrdinalIgnoreCase);

        if (globalPreferences.Count == 0)
            return true;

        var modFolderPath = skinEntry.Mod.FullPath;
        if (!Directory.Exists(modFolderPath)) return false;

        // 3. 获取并过滤 .ini 文件
        var allIniFiles = Directory.GetFiles(modFolderPath, "*.ini", SearchOption.AllDirectories);

        var validIniFiles = allIniFiles.Where(filePath =>
        {
            var relativePath = Path.GetRelativePath(modFolderPath, filePath);
            var parts = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            return !parts.Any(p => p.StartsWith(ModFolderHelpers.ALT_DISABLED_PREFIX, StringComparison.OrdinalIgnoreCase));
        });

        bool anyFileUpdated = false;

        // 4. 遍历处理
        foreach (var iniFile in validIniFiles)
        {
            var relativePath = Path.GetRelativePath(modFolderPath, iniFile);

            var fileContent = await File.ReadAllLinesAsync(iniFile).ConfigureAwait(false);

            var updatedContent = UpdateIniContent(fileContent, relativePath, globalPreferences, explicitNamespace != null, out bool isModified);

            if (isModified)
            {
                await File.WriteAllLinesAsync(iniFile, updatedContent).ConfigureAwait(false);
                anyFileUpdated = true;
                _logger.Information($"Synced preferences to {relativePath}");
            }
        }

        return anyFileUpdated;
    }

    /// <summary>
    /// Update local INI content by matching keys with relative path context
    /// </summary>
    private List<string> UpdateIniContent(
        string[] lines,
        string relativeFilePath,
        Dictionary<string, string> preferences,
        bool hasExplicitNamespace,
        out bool isModified)
    {
        var result = new List<string>(lines.Length);
        isModified = false;
        bool inConstantsSection = false;

        var variableRegex = new System.Text.RegularExpressions.Regex(
            @"^\s*(?<keywords>(?:global\s+|persist\s+)*)\$?(?<key>\w+)\s*=\s*(?<value>.*?)\s*(?<comment>(?:;|//).*)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var line in lines)
        {
            var trimLine = line.Trim();

            // 检测 Section
            if (trimLine.StartsWith("[") && trimLine.EndsWith("]"))
            {
                inConstantsSection = trimLine.Equals("[Constants]", StringComparison.OrdinalIgnoreCase);
                result.Add(line);
                continue;
            }

            if (inConstantsSection && !string.IsNullOrWhiteSpace(trimLine))
            {
                var match = variableRegex.Match(line);
                if (match.Success)
                {
                    var varName = match.Groups["key"].Value;

                    string lookupKey;

                    if (hasExplicitNamespace)
                    {
                        // 显式命名空间模式：
                        // d3dx_user.ini 里存的是 "$\Namespace\var=val"
                        lookupKey = varName.ToLower();
                    }
                    else
                    {
                        // 默认路径模式：
                        // d3dx_user.ini 里存的是 "$\Mods\...\folder\file.ini\var=val"
                        lookupKey = Path.Combine(relativeFilePath, varName).Replace('/', '\\').ToLower();
                    }

                    if (preferences.TryGetValue(lookupKey, out var newValue))
                    {
                        var currentValue = match.Groups["value"].Value;

                        if (!currentValue.Trim().Equals(newValue.Trim(), StringComparison.OrdinalIgnoreCase))
                        {
                            var keywords = match.Groups["keywords"].Value;
                            var comment = match.Groups["comment"].Value;

                            var indent = line.TakeWhile(char.IsWhiteSpace).ToArray();
                            var hasDollar = line.Contains($"${varName}", StringComparison.OrdinalIgnoreCase);
                            var varPart = hasDollar ? $"${varName}" : varName;

                            var newLine = $"{new string(indent)}{keywords}{varPart} = {newValue}";

                            if (!string.IsNullOrEmpty(comment))
                            {
                                newLine += $" {comment}";
                            }

                            result.Add(newLine);
                            isModified = true;
                            continue;
                        }
                    }
                }
            }

            result.Add(line);
        }

        return result;
    }


    public async Task ResetPreferencesAsync(bool resetOnlyEnabledMods)
    {
        var getOption = resetOnlyEnabledMods ? GetOptions.Enabled : GetOptions.All;
        var activeMods = _skinManagerService.GetAllMods(getOption);


        await Parallel.ForEachAsync(activeMods, async (characterSkinEntry, ct) =>
        {
            var modSettings =
                await characterSkinEntry.Mod.Settings.TryReadSettingsAsync(false, ct).ConfigureAwait(false);
            if (modSettings is null)
                return;

            modSettings.SetPreferences(null);
            await characterSkinEntry.Mod.Settings.SaveSettingsAsync(modSettings).ConfigureAwait(false);
        }).ConfigureAwait(false);
    }

    private List<IniPreference> FindExistingModPref(string rootModFolderPath, ICollection<string> lines,
        CharacterSkinEntry skinEntry)
    {
        string? explicitNamespace = TryGetExplicitNamespace(skinEntry.Mod.FullPath);

        var searchPref = CreateUserIniPreference(rootModFolderPath, skinEntry, null, explicitNamespace);
        var modNameSpace = searchPref.FullPath;

        var modIndexes = new List<IniPreference>();

        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines.ElementAt(i);

            if (line.StartsWith(modNameSpace, StringComparison.OrdinalIgnoreCase))
            {
                var content = line.Substring(modNameSpace.Length);

                var keyValue = content.Split("=", StringSplitOptions.TrimEntries);

                if (keyValue.Length != 2)
                    continue;

                var foundPref = CreateUserIniPreference(
                    rootModFolderPath,
                    skinEntry,
                    new KeyValuePair<string, string>(keyValue[0], keyValue[1]),
                    explicitNamespace
                );

                foundPref.Index = i;
                modIndexes.Add(foundPref);
            }
        }

        return modIndexes;
    }

    private IniPreference CreateUserIniPreference(
            string rootModFolderPath,
            CharacterSkinEntry skinEntry,
            KeyValuePair<string, string>? keyValueTuple = null,
            string? explicitNamespace = null)
    {
        // => $\mods\
        var rootPath =
            CreateModRootPrefix(rootModFolderPath);

        var prefix = ModFolderHelpers.DISABLED_PREFIX;
        var modName = skinEntry.Mod.Name;
        var cleanName = modName.StartsWith(prefix)
           ? modName[prefix.Length..]
           : modName;

        return new IniPreference(
            rootPath,
            skinEntry.ModList.Character.ModCategory.InternalName,
            skinEntry.ModList.Character.InternalName,
            cleanName,
            keyValueTuple,
            explicitNamespace);
    }

    /// <summary>
    /// 尝试从 Mod 的 .ini 文件中获取显式声明的 namespace。
    /// 规则：查找不在 [] 节下的 namespace = value
    /// </summary>
    private string? TryGetExplicitNamespace(string modFolderPath)
    {
        if (!Directory.Exists(modFolderPath)) return null;

        var iniFiles = Directory.GetFiles(modFolderPath, "*.ini", SearchOption.AllDirectories)
            .Where(path =>
            {
                var relative = Path.GetRelativePath(modFolderPath, path);
                var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return !parts.Any(p => p.StartsWith("DISABLED", StringComparison.OrdinalIgnoreCase));
            });

        // 匹配 namespace = value，忽略大小写，允许周围有空格
        // 捕获组 <ns> 提取值，遇到空白、换行或分号(注释)截止
        var namespaceRegex = new System.Text.RegularExpressions.Regex(
            @"^\s*namespace\s*=\s*(?<ns>[^;\s]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

        foreach (var file in iniFiles)
        {
            foreach (var line in File.ReadLines(file))
            {
                var trimmed = line.Trim();
                if (string.IsNullOrWhiteSpace(trimmed)) continue;
                if (trimmed.StartsWith(";") || trimmed.StartsWith("//")) continue;

                if (trimmed.StartsWith("["))
                    break;

                var match = namespaceRegex.Match(trimmed);
                if (match.Success)
                {
                    return match.Groups["ns"].Value;
                }
            }
        }

        return null;
    }

    private string CreateModRootPrefix(string rootModFolderPath)
    {
        var separator = Path.DirectorySeparatorChar;
        rootModFolderPath = rootModFolderPath.TrimEnd(separator);

        // => $\mods\
        return "$" + separator + rootModFolderPath.Split(separator).Last() +
               separator;
    }

    internal class IniPreference : IEquatable<IniPreference>
    {
        public int Index { get; set; } = -1;
        public string FullPath { get; }
        public string Category { get; }
        public string Character { get; }
        public string ModName { get; }

        public KeyValuePair<string, string>? KeyValuePair;

        public IniPreference(
            string modRoot,
            string category,
            string character,
            string modName,
            KeyValuePair<string, string>? keyValueTuple = null,
            string? explicitNamespace = null)
        {
            Category = category.ToLower();
            Character = character.ToLower();
            ModName = modName.ToLower();

            if (keyValueTuple is not null)
            {
                KeyValuePair = new KeyValuePair<string, string>(
                    keyValueTuple.Value.Key.ToLower(),
                    keyValueTuple.Value.Value.ToLower());
            }

            if (!string.IsNullOrWhiteSpace(explicitNamespace))
            {
                // 使用显式命名空间
                // 格式: $\MyNamespace\key = value
                FullPath = "$" + Path.DirectorySeparatorChar + explicitNamespace.ToLower() + Path.DirectorySeparatorChar;
            }
            else
            {
                // 使用基于路径的命名空间
                // 格式: $\Mods\Category\Character\ModName\key = value
                var separator = Path.DirectorySeparatorChar;
                FullPath = modRoot + category + separator + character + separator + modName + separator;
            }

            if (keyValueTuple is not null)
                FullPath += $"{KeyValuePair.Value.Key} = {KeyValuePair.Value.Value}";

            FullPath = FullPath.ToLower();
        }

        public override string ToString() => FullPath;

        public static implicit operator string(IniPreference iniPreference) => iniPreference.FullPath;

        [MemberNotNullWhen(true, nameof(KeyValuePair))]
        public bool HasKeyValue => KeyValuePair is not null;

        public bool KeyEquals(string key) => KeyValuePair?.Key.Equals(key, StringComparison.OrdinalIgnoreCase) ?? false;

        public bool ValueEquals(string value) =>
            KeyValuePair?.Value.Equals(value, StringComparison.OrdinalIgnoreCase) ?? false;

        public bool Equals(IniPreference? other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return FullPath.Equals(other.FullPath, StringComparison.OrdinalIgnoreCase);
        }

        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != GetType()) return false;
            return Equals((IniPreference)obj);
        }

        public override int GetHashCode()
        {
            var hashCode = new HashCode();
            hashCode.Add(FullPath, StringComparer.OrdinalIgnoreCase);
            return hashCode.ToHashCode();
        }

        public static bool operator ==(IniPreference? left, IniPreference? right)
        {
            return Equals(left, right);
        }

        public static bool operator !=(IniPreference? left, IniPreference? right)
        {
            return !Equals(left, right);
        }
    }
}