using Microsoft.Win32;
using System.Runtime.InteropServices;

namespace GIMI_ModManager.WinUI.Services;

/// <summary>
/// 精确判断当前进程是否真的“经过 UAC 提权”，避免管理员警告误报。
/// </summary>
/// <remarks>
/// 背景：<see cref="System.Security.Principal.WindowsPrincipal.IsInRole"/> 只要进程令牌中
/// Administrators 组处于启用状态就返回 true。但在以下场景中进程并未经过任何 UAC 提权，
/// 应用内“以管理员身份运行警告”会成为误报（GitHub Issue #65）：
///  1. 系统 UAC 被关闭（EnableLUA = 0），所有进程都使用完整管理员令牌；
///  2. 使用内置 Administrator 账户（默认不启用管理员审批模式）；
///  3. 安装程序以“以管理员身份运行”方式启动后自动拉起应用（子进程继承提权令牌）。
/// 因此仅当 TokenElevationType == Full（存在链接的受限令牌）且 UAC 开启时才视为“真提权”。
/// </remarks>
internal static class WindowsElevationService
{
    private const uint TokenQuery = 0x0008;

    // TOKEN_INFORMATION_CLASS
    private const int TokenElevationType = 18;

    // TOKEN_ELEVATION_TYPE
    private const int TokenElevationTypeFull = 2;

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass,
        IntPtr tokenInformation, uint tokenInformationLength, out uint returnLength);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    /// <summary>
    /// 判断系统是否启用了 UAC（EnableLUA）。
    /// </summary>
    public static bool IsUacEnabled()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Policies\System");
            if (key?.GetValue("EnableLUA") is int value)
                return value != 0;
        }
        catch
        {
            // 读取失败时按启用处理，保守地不改变原有行为。
        }

        return true;
    }

    /// <summary>
    /// 返回当前进程令牌的 TOKEN_ELEVATION_TYPE（1=Default，2=Full，3=Limited）。
    /// 查询失败时返回 0。
    /// </summary>
    public static int GetTokenElevationType()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenQuery, out var tokenHandle))
            return 0;

        try
        {
            if (GetTokenInformation(tokenHandle, TokenElevationType, out int elevationType))
                return elevationType;
            return 0;
        }
        finally
        {
            CloseHandle(tokenHandle);
        }
    }

    /// <summary>
    /// 当前进程是否真的经过 UAC 提权（存在链接的受限令牌且 UAC 开启）。
    /// UAC 被关闭或使用内置 Administrator 账户时返回 false，避免“管理员警告”误报。
    /// </summary>
    public static bool IsElevatedViaUac()
    {
        return IsUacEnabled() && GetTokenElevationType() == TokenElevationTypeFull;
    }

    private static bool GetTokenInformation<T>(IntPtr tokenHandle, int tokenInformationClass, out T tokenInformation)
        where T : struct
    {
        tokenInformation = default;
        var size = Marshal.SizeOf<T>();
        var buffer = Marshal.AllocHGlobal(size);
        try
        {
            if (!GetTokenInformation(tokenHandle, tokenInformationClass, buffer, (uint)size, out _))
                return false;

            tokenInformation = Marshal.PtrToStructure<T>(buffer);
            return true;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }
}