using GIMI_ModManager.Core.Contracts.Entities;
using GIMI_ModManager.Core.Entities.Mods.Contract;
using GIMI_ModManager.Core.Entities.Mods.FileModels;
using GIMI_ModManager.Core.Helpers;
using OneOf;
using System.Text.RegularExpressions;

namespace GIMI_ModManager.Core.Entities.Mods.SkinMod;

public partial class SkinModKeySwapManager(ISkinMod skinMod)
{
    private List<KeySwapSection>? _keySwaps;
    private static readonly Regex KeySectionPattern = KeySectionRegex();

    public void ClearKeySwaps() => _keySwaps = null;

    /// <summary>
    /// 读取所有INI文件中的键位交换配置
    /// </summary>
    public async Task<Dictionary<string, List<KeySwapSection>>> ReadAllKeySwapConfigurations(CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, List<KeySwapSection>>();
        var modDir = new DirectoryInfo(skinMod.FullPath);
        var iniFiles = modDir.EnumerateFiles("*.ini", SearchOption.AllDirectories);

        foreach (var iniFile in iniFiles)
        {
            var keySwaps = await ParseKeySwapsFromIni(iniFile.FullName, cancellationToken).ConfigureAwait(false);

            if (keySwaps.Count > 0)
            {
                var relativePath = Path.GetRelativePath(modDir.FullName, iniFile.FullName);
                result[relativePath] = keySwaps;
            }
        }

        return result;
    }

    /// <summary>
    /// 从单个INI文件解析键位交换配置
    /// </summary>
    private static async Task<List<KeySwapSection>> ParseKeySwapsFromIni(string filePath, CancellationToken cancellationToken)
    {
        var iniKeySwaps = new List<IniKeySwapSection>();
        var keySwapLines = new List<string>();
        var keySwapBlockStarted = false;
        var sectionLine = string.Empty;

        await foreach (var line in File.ReadLinesAsync(filePath, cancellationToken).ConfigureAwait(false))
        {
            // 跳过注释和空行
            if (line.Trim().StartsWith(';') || string.IsNullOrWhiteSpace(line))
                continue;

            // 检查是否需要结束当前键位交换块
            if (ShouldEndKeySwapBlock(line, keySwapBlockStarted, keySwapLines.Count))
            {
                ProcessCurrentKeySwapBlock(keySwapLines, sectionLine, iniKeySwaps);
                keySwapLines.Clear();

                // 检查新行是否是[key...]节
                keySwapBlockStarted = IsKeySection(line);
                sectionLine = keySwapBlockStarted ? line : string.Empty;
                continue;
            }

            // 开始新的[key...]节块
            if (IsKeySection(line))
            {
                keySwapBlockStarted = true;
                sectionLine = line;
                continue;
            }

            // 收集当前节块的行
            if (keySwapBlockStarted)
                keySwapLines.Add(line);
        }

        // 处理文件末尾的最后一个节
        if (keySwapBlockStarted && keySwapLines.Count > 0)
        {
            ProcessCurrentKeySwapBlock(keySwapLines, sectionLine, iniKeySwaps);
        }

        return [.. iniKeySwaps.Select(KeySwapSection.FromIniKeySwapSection)];
    }

    private static void ProcessCurrentKeySwapBlock(List<string> keySwapLines, string sectionLine, List<IniKeySwapSection> iniKeySwaps)
    {
        var keySwap = IniConfigHelpers.ParseKeySwap(keySwapLines, sectionLine);
        keySwap ??= new IniKeySwapSection
        {
            SectionKey = sectionLine,
            ForwardKeys = [],
            BackwardKeys = [],
            Type = string.Empty
        };
        iniKeySwaps.Add(keySwap);
    }

    private static bool IsKeySection(string line)
    {
        return IniConfigHelpers.IsSection(line) && KeySectionPattern.IsMatch(line.Trim());
    }

    private static bool ShouldEndKeySwapBlock(string line, bool isBlockActive, int lineCount)
    {
        return isBlockActive && (IsKeySection(line) || lineCount > 9);
    }

    /// <summary>
    /// 保存所有键位交换配置到INI文件
    /// </summary>
    public async Task SaveAllKeySwapConfigurations(Dictionary<string, List<KeySwapSection>> allKeySwaps,
        CancellationToken cancellationToken = default)
    {
        var modDir = new DirectoryInfo(skinMod.FullPath);

        foreach (var (relativePath, keySwaps) in allKeySwaps)
        {
            if (keySwaps.Count == 0)
                continue;

            var iniFilePath = Path.Combine(modDir.FullName, relativePath);
            var iniFile = new FileInfo(iniFilePath);

            if (!iniFile.Exists)
                continue;

            var fileLines = await File.ReadAllLinesAsync(iniFilePath, cancellationToken).ConfigureAwait(false);
            var linesList = new List<string>(fileLines);

            // 分离节名变更和未变更的配置
            var (changedKeySwaps, unchangedKeySwaps) = SeparateKeySwaps(keySwaps);

            // 处理节名变更的配置
            await ProcessChangedKeySwaps(linesList, changedKeySwaps).ConfigureAwait(false);

            // 处理节名未变更的配置
            await ProcessUnchangedKeySwaps(linesList, unchangedKeySwaps).ConfigureAwait(false);

            // 写入文件
            await WriteLinesToFile(iniFilePath, linesList, cancellationToken).ConfigureAwait(false);
        }
    }

    private (List<KeySwapSection> changed, List<KeySwapSection> unchanged) SeparateKeySwaps(List<KeySwapSection> keySwaps)
    {
        var changed = new List<KeySwapSection>();
        var unchanged = new List<KeySwapSection>();

        foreach (var keySwap in keySwaps)
        {
            if (!string.IsNullOrWhiteSpace(keySwap.OriginalSectionName) &&
                !string.Equals(keySwap.OriginalSectionName, keySwap.SectionName, StringComparison.OrdinalIgnoreCase))
            {
                changed.Add(keySwap);
            }
            else
            {
                unchanged.Add(keySwap);
            }
        }

        return (changed, unchanged);
    }

    private Task ProcessChangedKeySwaps(List<string> fileLines, List<KeySwapSection> changedKeySwaps)
    {
        foreach (var keySwap in changedKeySwaps)
        {
            var (startIndex, endIndex) = FindSectionBounds(fileLines, keySwap.OriginalSectionName!);
            if (startIndex == -1)
                continue;

            var sectionLines = fileLines.GetRange(startIndex, endIndex - startIndex);
            UpdateSectionContent(sectionLines, keySwap);

            fileLines.RemoveRange(startIndex, endIndex - startIndex);
            fileLines.InsertRange(startIndex, sectionLines);
        }

        return Task.CompletedTask;
    }

    private Task ProcessUnchangedKeySwaps(List<string> fileLines, List<KeySwapSection> unchangedKeySwaps)
    {
        var sectionStartMap = CreateSectionStartMap(fileLines, unchangedKeySwaps);

        for (var i = unchangedKeySwaps.Count - 1; i >= 0; i--)
        {
            var keySwap = unchangedKeySwaps[i];
            if (!sectionStartMap.TryGetValue(keySwap.SectionName, out var sectionStartIndex))
                continue;

            UpdateSectionKeyValues(fileLines, keySwap, sectionStartIndex);
        }

        return Task.CompletedTask;
    }

    private static (int start, int end) FindSectionBounds(List<string> lines, string sectionName)
    {
        for (var i = 0; i < lines.Count; i++)
        {
            if (!IniConfigHelpers.IsSection(lines[i], sectionName)) continue;
            var endIndex = lines.Count;
            for (var j = i + 1; j < lines.Count; j++)
            {
                if (!IniConfigHelpers.IsSection(lines[j])) continue;
                endIndex = j;
                break;
            }

            return (i, endIndex);
        }

        return (-1, -1);
    }

    private static Dictionary<string, int> CreateSectionStartMap(List<string> lines, List<KeySwapSection> keySwaps)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        foreach (var keySwap in keySwaps.Where(keySwap => !map.ContainsKey(keySwap.SectionName)))
        {
            for (var i = 0; i < lines.Count; i++)
            {
                if (!IniConfigHelpers.IsSection(lines[i], keySwap.SectionName)) continue;
                map[keySwap.SectionName] = i;
                break;
            }
        }

        return map;
    }

    /// <summary>
    /// 更新节内容（保持原有非key/back行的顺序）
    /// </summary>
    private void UpdateSectionContent(List<string> sectionLines, KeySwapSection keySwap)
    {
        if (sectionLines.Count == 0)
            return;

        // 更新节名
        sectionLines[0] = keySwap.SectionName;

        // 找到原有key/back行的位置范围
        var (firstKeyIndex, lastKeyIndex) = FindKeyLinesRange(sectionLines);

        // 移除原有key/back行
        if (firstKeyIndex != -1)
        {
            sectionLines.RemoveRange(firstKeyIndex, lastKeyIndex - firstKeyIndex + 1);
        }

        // 确定插入位置（原有key/back行的起始位置，若无则在节末尾）
        var insertIndex = firstKeyIndex != -1 ? firstKeyIndex : sectionLines.Count;

        // 插入新的key/back行
        InsertNewKeySwapLines(sectionLines, keySwap, insertIndex);
    }

    /// <summary>
    /// 查找节中key/back行的位置范围
    /// </summary>
    private static (int firstIndex, int lastIndex) FindKeyLinesRange(List<string> sectionLines)
    {
        var firstIndex = -1;
        var lastIndex = -1;

        for (var i = 1; i < sectionLines.Count; i++) // 从1开始跳过节名行
        {
            var line = sectionLines[i];
            if (!IniConfigHelpers.IsIniKey(line, IniKeySwapSection.ForwardIniKey) &&
                !IniConfigHelpers.IsIniKey(line, IniKeySwapSection.BackwardIniKey)) continue;
            if (firstIndex == -1)
                firstIndex = i;
            lastIndex = i;
        }

        return (firstIndex, lastIndex);
    }

    /// <summary>
    /// 在指定位置插入新的key/back行
    /// </summary>
    private static void InsertNewKeySwapLines(List<string> sectionLines, KeySwapSection keySwap, int insertIndex)
    {
        var currentIndex = insertIndex;

        // 插入forward keys
        foreach (var forwardKey in keySwap.ForwardKeys)
        {
            if (IniConfigHelpers.FormatIniKey(IniKeySwapSection.ForwardIniKey, forwardKey) is { } line)
            {
                sectionLines.Insert(currentIndex++, line);
            }
        }

        // 插入backward keys
        foreach (var backwardKey in keySwap.BackwardKeys)
        {
            if (IniConfigHelpers.FormatIniKey(IniKeySwapSection.BackwardIniKey, backwardKey) is { } line)
            {
                sectionLines.Insert(currentIndex++, line);
            }
        }
    }

    /// <summary>
    /// 更新节内的key和back键值对（保持原有顺序）
    /// </summary>
    private void UpdateSectionKeyValues(List<string> fileLines, KeySwapSection keySwap, int sectionStartIndex)
    {
        // 找到节的结束位置
        var sectionEndIndex = fileLines.Count;
        for (var i = sectionStartIndex + 1; i < fileLines.Count; i++)
        {
            if (IniConfigHelpers.IsSection(fileLines[i]))
            {
                sectionEndIndex = i;
                break;
            }
        }

        // 获取节内所有行（包含节名）
        var sectionLines = fileLines.GetRange(sectionStartIndex, sectionEndIndex - sectionStartIndex);

        // 更新节内容（保持顺序）
        UpdateSectionContent(sectionLines, keySwap);

        // 替换原节内容
        fileLines.RemoveRange(sectionStartIndex, sectionEndIndex - sectionStartIndex);
        fileLines.InsertRange(sectionStartIndex, sectionLines);
    }


    private static async Task WriteLinesToFile(string filePath, List<string> lines, CancellationToken cancellationToken)
    {
        await using var writeStream = new FileStream(filePath, FileMode.Truncate, FileAccess.Write, FileShare.None);
        await using var writer = new StreamWriter(writeStream);

        foreach (var line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();
            await writer.WriteLineAsync(line).ConfigureAwait(false);
        }
    }

    public OneOf<KeySwapSection[], KeySwapsNotLoaded> GetKeySwaps()
    {
        return _keySwaps is null
            ? new KeySwapsNotLoaded()
            : _keySwaps.ToArray();
    }

    [GeneratedRegex(@"^\[key.*\]$", RegexOptions.IgnoreCase, "zh-CN")]
    private static partial Regex KeySectionRegex();
}

public struct KeySwapsNotLoaded
{
}