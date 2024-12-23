# JASM - 只是又一款皮肤管理器（汉化版）

> 原项目地址：[https://github.com/Jorixon/JASM](https://github.com/Jorixon/JASM)

**这仍处于开发的早期阶段。请自行备份并自行承担风险⚠️**

未处理的异常也会写入日志文件。可以在 appsettings.json 中启用调试日志记录

## 功能
- 精美的用户界面 👀
- 可将文件直接拖放到应用程序中，支持加密压缩包
- 自自动将未分类的模组分类到相应角色的文件夹中
- 在不同角色之间移动模组
- 可直接从应用程序启动 3Dmigto 启动器和 / 或某款游戏
- 应用程序会监控角色文件夹，若文件夹中的皮肤有增减，会自动更新
- 编辑 merged.ini 键值
- 将 JASM 管理的所有模组导出（复制）到用户指定的文件夹
- 使用 F10 键或应用程序中的刷新按钮刷新模组。（需要一个提升权限的辅助进程，详见下文说明）

## 快捷键
- “空格键” - 在角色视图中，切换所选模组的启用 / 禁用状态
- “F10” - 如果提升权限的辅助进程以及某款游戏正在运行，可刷新游戏中的模组
- “F5” - 在角色视图中，从磁盘刷新角色的模组
- “CTRL + F” - 在角色概览界面，聚焦到搜索栏
- “Esc” - 在角色视图中，返回角色概览界面
- “F1” - 在角色视图中，打开游戏内可选皮肤
- “CTRL + O” - 在角色详情界面添加压缩包形式的模组

## 下载
最新版本可从 GameBanana 或者[Releases](https://github.com/Moonholder/JASM/releases) 页面下载。要启动应用程序，请在 ```JASM/``` 文件夹中运行 ```JASM - Just Another Skin Manager.exe```，建议为此创建一个快捷方式。

## 系统要求
- Windows 10 1809 版本或更高版本（[据称](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/))
- [.NET 桌面运行时](https://aka.ms/dotnet-core-applaunch?missing_runtime=true&arch=x64&rid=win10-x64&apphost_version=9.0.0&gui=true)
- [Windows App SDK](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/downloads)

如果未下载这些，应用程序会提示你下载必要的依赖项并提供相应链接。

### 提升权限的辅助进程
提升权限的辅助进程是一个小程序，可从应用程序中以提升权限的方式启动。它完全是可选的，算是一个小众功能。  
它用于向游戏发送 F10 键来刷新模组。在 JASM 中启用和禁用模组也会自动刷新模组。这是通过命名管道实现的。  
该进程不会监听按键绑定，它只等待来自应用程序的简单 “1” 命令，然后就会向游戏发送 F10 键。

使用了[H.InputSimulator](https://github.com/HavenDV/H.InputSimulator) 库来发送键盘输入。

## FAQ

### Tips

应用程序设置存储在这里```C:\Users\<username>\AppData\Local\JASM\ApplicationData```

Mod 特定设置存储在 mod 文件夹中，并以```.JASM_``` 为前缀。导出 mod 时，可以忽略这些文件。

JASM会识别Mod文件夹中的【```merged.ini```、```Script.ini```】和```Master```为前缀的ini文件，并提取其中的按键切换信息，您可以在角色详情页面的按键切换面板中编辑。


### JASM 不能启动

我认为这是由于 WinAppSdk 安装不正确而导致的一些异常。我不知道是什么原因造成的。一个临时（永久？）解决方案是使用不需要 WinAppSdk 或 .NET 的独立版本的 JASM。请参阅发布页面 [SelfContainted_JASM_vx.x.x.7z](https://github.com/Moonholder/JASM/releases)。参考 [#72](https://github.com/Jorixon/JASM/issues/72) 和 [#171](https://github.com/Jorixon/JASM/issues/171)

如果 JASM 之前能正常工作，另一个可能的修复方法是删除 JASM 的用户设置文件夹。这会清除你的设置，比如预设、文件夹路径等。不过，你的模组以及模组设置（如自定义显示名称和图片）不会受到影响。JASM 设置存储在以下位置：`%localappdata%\JASM` / `C:\Users\<username>\AppData\Local\JASM`。你可以先尝试删除每个游戏的设置文件夹，看看是否有帮助，或者也可以直接删除整个文件夹。预设存储在预设文件夹内。最好先备份一下。

### 命令行支持

JASM 具备基本的命令行支持。截至目前，唯一支持的功能是直接启动进入选定的游戏。如果你希望看到更多命令行选项，欢迎针对你建议的使用场景提出问题。

有关更多信息，请参阅 --help。

Powershell：
```powershell
.\'JASM - Just Another Skin Manager.exe' --help
# 示例：如果当前实例正在运行，则关闭它并使用选定的游戏启动 JASM
.\'JASM - Just Another Skin Manager.exe' --switch --game genshin
```

### 内存使用率高

每切换一次页面就会分配大量内存且不会释放，这会导致在页面间快速切换时应用程序很快就会占用超过 1GB 的内存。这不是一个能快速解决的问题。如果你发现程序运行变慢，我建议重启应用程序。

根据调查，WinUI 在导航页面时似乎可能有内存泄漏。大多数内存都是非托管内存，这意味着内存分析器不会有太大帮助。

### 提升权限的辅助进程下载链接

由于提升权限的辅助进程会被标记为恶意软件，你需要从 [发布页面](https://github.com/Jorixon/JASM/releases/tag/v2.14.3) 手动下载它。