using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Security.Principal;
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
    public static void Main(string[] args)
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
            StartPipeServer(userName);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine(e);
            Environment.Exit(1);
        }
    }


    static void StartPipeServer(string userName)
    {
        var specificUserAccount = new NTAccount(userName);
        var specificUserSid = (SecurityIdentifier)specificUserAccount.Translate(typeof(SecurityIdentifier));

        var ps = new PipeSecurity();

        var userAccessRule = new PipeAccessRule(specificUserSid,
            PipeAccessRights.FullControl, AccessControlType.Allow);
        ps.AddAccessRule(userAccessRule);

        while (true)
        {
            using var pipeServer = NamedPipeServerStreamConstructors.New("MyPipess", PipeDirection.In, 1,
                PipeTransmissionMode.Message,
                PipeOptions.Asynchronous, pipeSecurity: ps);
            Console.WriteLine("Waiting for connection...");

            pipeServer.WaitForConnection();
            Console.WriteLine("Connected!");
            Console.WriteLine("----------------------");

            using var reader = new StreamReader(pipeServer);
            var command = reader.ReadLine();
            Console.WriteLine("Received command: " + command);
            Console.WriteLine("From user: " + pipeServer.GetImpersonationUserName());

            // 支持命令格式 0:Genshin 0:Honkai 0:WuWa 0:ZZZ
            if (command != null && command.StartsWith("0"))
            {
                var game = "Genshin";
                if (command.Contains(":"))
                {
                    var parts = command.Split(':');
                    if (parts.Length > 1)
                        game = parts[1].Trim();
                }
                Console.WriteLine($"Refreshing {game} Mods");
                RefreshGameMods(game);
                continue;
            }

            switch (command)
            {
                case "-2":
                    break;
                case "-1":
                    Console.WriteLine("Exiting");
                    Environment.Exit(0);
                    return;
                default:
                    Console.Error.WriteLine($"Unknown command: {command}");
                    break;
            }
        }
    }

    [DllImport("User32.dll")]
    static extern int SetForegroundWindow(IntPtr point);


    static void RefreshGameMods(string game)
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

        _ = SetForegroundWindow(ptr.Value);

        new InputSimulator().Keyboard
            .KeyDown(VirtualKeyCode.F10)
            .Sleep(100)
            .KeyUp(VirtualKeyCode.F10)
            .Sleep(100);
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