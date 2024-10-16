# JASM - 另一个皮肤管理器（汉化版）

> 原项目地址：[https://github.com/Jorixon/JASM](https://github.com/Jorixon/JASM)

**这仍处于开发的早期阶段。请自行备份并自行承担风险⚠️**

未处理的异常也会写入日志文件。可以在 appsettings.json 中启用调试日志记录

## 功能
- 漂亮的 UI 👀
- 将模组文件直接拖放到应用程序中，支持加密压缩包
- 自动将未分类的模组分类到各个角色的文件夹中
- 在角色之间移动模组
- 直接从应用程序启动 3Dmigto 启动器和/或某个游戏
- 应用程序监视角色文件夹，如果在文件夹中添加或删除皮肤，则自动更新。
- 编辑 merged.ini 键
- 将 JASM 管理的所有模组导出（复制）到用户指定的文件夹
- 使用 F10 或应用程序中的刷新按钮刷新模组。 （需要提升侧进程，见下文说明）

## 快捷键
- “SPACE” - 在角色视图中，打开/关闭所选模组
- “F10” - 如果Elevator进程和某个游戏正在运行，则刷新游戏中的模组
- “F5” - 在角色视图中，从磁盘刷新角色的模组
- “CTRL + F” - 在角色概览中，将焦点放在搜索栏上
- “ESCAPE” - 在角色视图中，返回角色概览
- “F1” - 在角色视图中，打开可选择的游戏内皮肤

## 下载
可以从 GameBanana 或 [Releases](https://github.com/Moonholder/JASM/releases) 页面下载最新版本。要启动应用程序，请在 ```JASM/``` 文件夹中运行 ```JASM - Just Another Skin Manager.exe```，我建议为其创建快捷方式。

## 要求
- Windows 10，版本 1809 或更高版本（[据称](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/))
- [NET 桌面运行时](https://aka.ms/dotnet-core-applaunch?missing_runtime=true&arch=x64&rid=win10-x64&apphost_version=8.0.0&gui=true)
- [Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)

如果您尚未下载这些，应用程序将提示您下载必要的依赖项并提供链接。

### Elevator 进程
Elevator进程是一个小程序，可以作为提升进程从应用程序启动。它是完全可选的，是一种小众功能。
它用于将 F10 键发送到游戏以刷新模组。在 JASM 中启用和禁用模组也会自动刷新模组。这是通过命名管道完成的。
该过程不监听键绑定，只等待来自应用程序的简单“1”命令。这使得它将 F10 键发送到游戏。

[H.InputSimulator](https://github.com/HavenDV/H.InputSimulator) 库用于发送键盘输入。

## FAQ

### Tips

应用程序设置存储在这里```C:\Users\<username>\AppData\Local\JASM\ApplicationData```

Mod 特定设置存储在 mod 文件夹中，并以```.JASM_``` 为前缀。导出 mod 时，可以忽略这些文件。

JASM会识别Mod文件夹中的【```merged.ini```、```Script.ini```】和```Master```为前缀的ini文件，并提取其中的按键切换信息，您可以在角色详情页面的按键切换面板中编辑。


### JASM 不能启动

我认为这是由于 WinAppSdk 安装不正确而导致的一些异常。我不知道是什么原因造成的。一个临时（永久？）解决方案是使用不需要 WinAppSdk 或 .NET 的独立版本的 JASM。请参阅发布页面 [SelfContainted_JASM_vx.x.x.7z](https://github.com/Moonholder/JASM/releases)。参考 [#72](https://github.com/Jorixon/JASM/issues/72) 和 [#171](https://github.com/Jorixon/JASM/issues/171)

如果 JASM 以前可以正常工作，另一个可能的解决方法是删除 JASM 用户设置文件夹。这将清除您的设置，即预设、文件夹路径等。但是，您的模组以及模组设置（如自定义显示名称和图像）将保持不变。JASM 设置存储在此处：`%localappdata%\JASM` / `C:\Users\<username>\AppData\Local\JASM`。您可以先删除每个游戏设置文件夹，看看是否有帮助，或者直接删除整个文件夹。预设存储在预设文件夹中。最好先备份。

### 命令行支持

JASM 具有基本的命令行支持。截至目前，唯一支持的功能是直接启动选定的游戏。如果您想查看更多命令行选项，请随时打开一个问题，其中包含您建议的用例。

有关更多信息，请参阅 --help。

Powershell：
```powershell
.\'JASM - Just Another Skin Manager.exe' --help
# 示例：如果当前实例正在运行，则关闭它并使用选定的游戏启动 JASM
.\'JASM - Just Another Skin Manager.exe' --switch --game genshin
```

### 内存使用率高

对于每个导航页面，都会分配大量内存但未释放。这会导致应用程序通过在页面之间快速导航而快速使用超过 1GB 的内存。这不是一个快速修复。如果您发现它变慢，建议重新启动应用程序。

根据调查，WinUI 在导航页面时似乎可能有内存泄漏。大多数内存都是非托管内存，这意味着内存分析器不会有太大帮助。

### Elevator 下载链接

由于 Elevator 被标记为恶意软件，您需要从 [发布页面](https://github.com/Jorixon/JASM/releases/tag/v2.14.3) 手动下载它