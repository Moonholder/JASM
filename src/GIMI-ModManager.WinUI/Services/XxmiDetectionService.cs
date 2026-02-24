using Microsoft.Win32;
using Serilog;

namespace GIMI_ModManager.WinUI.Services;

public static class XxmiDetectionService
{
    private static readonly ILogger Logger = Log.ForContext(typeof(XxmiDetectionService));

    private const string UninstallRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";
    private const string XxmiLauncherPrefix = "XXMI Launcher";
    private const string LauncherExeRelativePath = @"Resources\Bin\XXMI Launcher.exe";

    /// <summary>
    /// 尝试从注册表检测 XXMI Launcher 的安装路径。
    /// 扫描 HKLM 和 HKCU 下的 Uninstall 注册表项，查找 "XXMI Launcher" 开头的子键，
    /// 读取其 InstallLocation 值并验证 XXMI Launcher.exe 是否存在。
    /// </summary>
    /// <returns>XXMI Launcher 安装根目录，或 null 表示未检测到。</returns>
    public static string? TryDetectXxmiLauncherPath()
    {
        // 优先检查 HKLM (MSI 安装)
        var path = SearchUninstallRegistry(Registry.LocalMachine);
        if (path != null) return path;

        // 检查 HKCU (用户级安装)
        path = SearchUninstallRegistry(Registry.CurrentUser);
        if (path != null) return path;

        // 尝试默认 %AppData% 路径
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            XxmiLauncherPrefix);

        if (ValidateXxmiPath(appDataPath))
        {
            Logger.Information("XXMI Launcher detected at default AppData path: {Path}", appDataPath);
            return appDataPath;
        }

        Logger.Debug("XXMI Launcher not detected");
        return null;
    }

    /// <summary>
    /// 获取 Mod 加载器文件夹路径
    /// </summary>
    public static string GetModLoaderPath(string xxmiRoot, string gameModelImporterShortName)
        => Path.Combine(xxmiRoot, gameModelImporterShortName);

    /// <summary>
    /// 获取 Mods 文件夹路径
    /// </summary>
    public static string GetModsPath(string xxmiRoot, string gameModelImporterShortName)
        => Path.Combine(xxmiRoot, gameModelImporterShortName, "Mods");

    /// <summary>
    /// 获取 XXMI Launcher 可执行文件路径。
    /// </summary>
    public static string GetLauncherExePath(string xxmiRoot)
        => Path.Combine(xxmiRoot, LauncherExeRelativePath);

    private static string? SearchUninstallRegistry(RegistryKey rootKey)
    {
        try
        {
            using var uninstallKey = rootKey.OpenSubKey(UninstallRegistryPath);
            if (uninstallKey == null) return null;

            foreach (var subKeyName in uninstallKey.GetSubKeyNames())
            {
                if (!subKeyName.StartsWith(XxmiLauncherPrefix, StringComparison.OrdinalIgnoreCase))
                    continue;

                try
                {
                    using var appKey = uninstallKey.OpenSubKey(subKeyName);
                    var installLocation = appKey?.GetValue("InstallLocation") as string;

                    if (string.IsNullOrWhiteSpace(installLocation))
                        continue;

                    if (ValidateXxmiPath(installLocation))
                    {
                        Logger.Information(
                            "XXMI Launcher detected via registry key '{KeyName}' at: {Path}",
                            subKeyName, installLocation);
                        return installLocation;
                    }
                }
                catch (Exception ex)
                {
                    Logger.Warning(ex, "Failed to read registry sub-key: {SubKey}", subKeyName);
                }
            }
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Failed to search uninstall registry under {RootKey}", rootKey.Name);
        }

        return null;
    }

    /// <summary>
    /// 验证给定路径是否为有效的 XXMI Launcher 安装目录。
    /// </summary>
    private static bool ValidateXxmiPath(string path)
    {
        if (!Directory.Exists(path))
            return false;

        var exePath = GetLauncherExePath(path);
        return File.Exists(exePath);
    }
}
