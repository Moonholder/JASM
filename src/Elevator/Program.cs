using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading.Tasks;
using WindowsInput;


// Exit codes:
// 0: Success
// 1: Unhandled exception
// 2: Bad arguments

// Commands:
// -2: Alive check
// -1: Exit
// 0: RefreshActiveGenshinMods

internal class Program
{
    private static readonly SemaphoreSlim _actionLock = new(1, 1);

    private static readonly InputSimulator _inputSimulator = new InputSimulator();
    static async Task Main(string[] args)
    {
        var userName = "";
        try
        {
            userName = args.First();
        }
        catch
        {
            Console.Error.WriteLine("Please provide a username");
            Environment.Exit(2);
        }

        try
        {
            var specificUserAccount = new NTAccount(userName);
            var specificUserSid = (SecurityIdentifier)specificUserAccount.Translate(typeof(SecurityIdentifier));

            var ps = new PipeSecurity();

            var userAccessRule = new PipeAccessRule(specificUserSid,
                PipeAccessRights.FullControl, AccessControlType.Allow);

            ps.AddAccessRule(userAccessRule);

            await StartPipeServerAsync(ps);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            Environment.Exit(1);
        }
    }


    static async Task StartPipeServerAsync(PipeSecurity ps)
    {
        while (true)
        {
            try
            {
                var pipeServer = NamedPipeServerStreamConstructors.New("MyPipess",
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Message,
                    PipeOptions.Asynchronous,
                    pipeSecurity: ps);

                Console.WriteLine("Waiting for connection...");
                await pipeServer.WaitForConnectionAsync();

                _ = HandleClientAsync(pipeServer);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Listener error: {ex.Message}");
                await Task.Delay(1000);
            }
        }
    }

    static async Task HandleClientAsync(NamedPipeServerStream pipeServer)
    {
        await using (pipeServer)
        using (var reader = new StreamReader(pipeServer))
        using (var writer = new StreamWriter(pipeServer) { AutoFlush = true })
        {
            try
            {
                var command = await reader.ReadLineAsync();
                if (string.IsNullOrEmpty(command)) return;

                await _actionLock.WaitAsync();
                try
                {
                    if (command.StartsWith("0"))
                    {
                        var game = "Genshin";
                        int clientPid = -1;
                        if (command.Contains(':'))
                        {
                            var parts = command.Split(':');
                            if (parts.Length > 1)
                                game = parts[1].Trim();

                            if (parts.Length > 2 && int.TryParse(parts[2], out int pid))
                            {
                                clientPid = pid;
                            }
                        }

                        Console.WriteLine($"Refreshing {game}...");

                        RefreshGameMods(game, clientPid);

                        await writer.WriteLineAsync("OK");
                        Console.WriteLine("Sent OK to client.");
                    }
                }
                finally
                {
                    _actionLock.Release();
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Error: {ex.Message}");
            }
        }
    }

    [DllImport("user32.dll")]
    static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    static extern int SetForegroundWindow(IntPtr point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    private const int SW_RESTORE = 9;

    static void RefreshGameMods(string game, int restorePid)
    {
        // 支持的游戏及其进程名
        var gameProcessMap = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "Genshin", new[] { "GenshinImpact", "YuanShen" } },
            { "Honkai", new[] { "StarRail" } },
            { "WuWa", new[] { "Client-Win64-Shipping" } },
            { "ZZZ", new[] { "ZenlessZoneZero" } }
        };

        if (!gameProcessMap.TryGetValue(game, out var processNames))
        {
            Console.Error.WriteLine($"Unknown game: {game}");
            return;
        }

        IntPtr? ptr = null;
        foreach (var processName in processNames)
        {
            ptr = GetGameProcess(processName, true);
            if (ptr != null) break;
        }

        if (ptr == null)
        {
            Console.Error.WriteLine($"Game process for {game} not found (tried: {string.Join(", ", processNames)}).");
            return;
        }

        IntPtr gameHandle = ptr.Value;

        if (gameHandle != IntPtr.Zero)
        {
            ShowWindow(gameHandle, SW_RESTORE);
            SetForegroundWindow(gameHandle);

            Thread.Sleep(300);

            _inputSimulator.Keyboard
            .KeyDown(VirtualKeyCode.F10)
            .Sleep(100)
            .KeyUp(VirtualKeyCode.F10);

            Thread.Sleep(100);
        }

        if (restorePid > 0)
        {
            Thread.Sleep(100);

            try
            {
                Console.WriteLine($"Restoring focus to client (PID: {restorePid})...");
                var clientProc = Process.GetProcessById(restorePid);

                if (clientProc != null && !clientProc.HasExited && clientProc.MainWindowHandle != IntPtr.Zero)
                {
                    ShowWindow(clientProc.MainWindowHandle, SW_RESTORE);
                    SetForegroundWindow(clientProc.MainWindowHandle);
                }
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to restore client focus: {ex.Message}");
            }
        }
    }

    static IntPtr? GetGameProcess(string processName, bool silent = false)
    {
        var processes = Process.GetProcessesByName(processName);

        if (processes.Length == 0)
        {
            if (!silent)
            {
                Console.Error.WriteLine($"{processName} process not found");
            }
            return null;
        }

        if (processes.Length > 1)
        {
            Console.Error.WriteLine($"Multiple {processName} processes found");
        }

        var process = processes.FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);

        if (process == null)
        {
            if (!silent)
            {
                Console.Error.WriteLine($"{processName} process found, but main window handle is not available.");
            }
            return null;
        }

        return process.MainWindowHandle;
    }
}

/*[DllImport("user32.dll")]
static extern bool PostMessage(IntPtr hWnd, UInt32 Msg, int wParam, int lParam);

const UInt32 WM_KEYDOWN = 0x0100;
const int VK_F10 = 0x79;

async Task RefreshGenshinMods()
{
    var ptr = GetGenshinProcess().MainWindowHandle;


    SetForegroundWindow(ptr);
    await Task.Delay(100);

    var success = PostMessage(ptr, WM_KEYDOWN, VK_F10, 0);

    Console.WriteLine(!success ? "Failed to send message" : "Sent message");
}*/

/*async Task RefreshGenshinModsWinInput()
{
    var ptr = GetGenshinProcess().MainWindowHandle;

    SetForegroundWindow(ptr);
    await Task.Delay(1000);

    await WindowsInput.Simulate.Events()
        .Click(KeyCode.F10)
        .Invoke();
}*/