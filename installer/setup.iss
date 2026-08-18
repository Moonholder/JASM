; JASM - Just Another Skin Manager
; Inno Setup Script
; https://jrsoftware.org/ishelp/

#define MyAppName "JASM - Just Another Skin Manager"
#define MyAppExeName "JASM - Just Another Skin Manager.exe"
#define MyAppPublisher "Moonholder"
#define MyAppURL "https://github.com/Moonholder/JASM"

#define PublishDir "..\output\JASM"
#ifndef MyAppVersion
#define MyAppVersion "0.0.0"
#endif

[Setup]
AppId={{A7B8C9D0-E1F2-4A5B-8C7D-0123456789AB}
AppMutex=JASM_WinUI_App_Mutex
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} v{#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}/issues
AppUpdatesURL={#MyAppURL}/releases
DefaultDirName={localappdata}\Programs\JASM
DefaultGroupName=JASM
DisableProgramGroupPage=yes
; 不需要管理员权限
PrivilegesRequired=lowest
; 输出设置
OutputDir=output
OutputBaseFilename=JASM_v{#MyAppVersion}_Setup
; 压缩设置
Compression=lzma2/ultra64
SolidCompression=yes
LZMADictionarySize=65536
; UI 设置
WizardStyle=modern
; 关闭/重启应用
CloseApplications=yes
CloseApplicationsFilter=*.exe
RestartApplications=yes
; 版本信息
VersionInfoVersion={#MyAppVersion}
VersionInfoCompany={#MyAppPublisher}
VersionInfoProductName={#MyAppName}
; 卸载时不删除用户数据
UninstallDisplayName={#MyAppName}

[Languages]
Name: "chinesesimplified"; MessagesFile: "ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; 主程序文件
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; WebView2 Evergreen Runtime（在线商店/Mod 页面依赖，安装后删除临时文件）
Source: "redist\MicrosoftEdgeWebview2Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not WebView2RuntimeInstalled

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 安装 WebView2 Evergreen Runtime（仅在未检测到运行时，失败不影响主程序安装）
Filename: "{tmp}\MicrosoftEdgeWebview2Setup.exe"; Parameters: "/silent /install"; StatusMsg: "正在安装 WebView2 运行时..."; Check: not WebView2RuntimeInstalled; Flags: waituntilterminated
; 手动安装模式下提供启动复选框
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent shellexec runasoriginaluser; Check: ShouldLaunchApp
; 静默安装完成后的强制自启动
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Flags: nowait shellexec runasoriginaluser; Check: IsAutoRun and ShouldLaunchApp

[UninstallDelete]
; 卸载时清理可能生成的运行时文件，但不删除用户数据（%LOCALAPPDATA%\JASM 中的设置）
Type: files; Name: "{app}\*.log"
Type: dirifempty; Name: "{app}"

[Code]
// 检查 WebView2 Evergreen Runtime 是否已安装
// 官方客户端 GUID: {F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}
// 同时检查 64/32 位注册表视图与当前用户级安装，避免误判
function WebView2RuntimeInstalled: Boolean;
var
  Version: String;
begin
  Result := False;
  if RegQueryStringValue(HKLM,
      'SOFTWARE\WOW6432Node\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', Version) or
     RegQueryStringValue(HKLM,
      'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', Version) or
     RegQueryStringValue(HKCU,
      'SOFTWARE\Microsoft\EdgeUpdate\Clients\{F3017226-FE2A-4295-8BDF-00C3A9A7E4C5}',
      'pv', Version) then
  begin
    Result := True;
    Log('WebView2 Runtime found: ' + Version);
  end
  else
    Log('WebView2 Runtime not found, will install bootstrapper');
end;

// 自定义卸载提示：提醒用户设置数据保留
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    MsgBox('JASM 已卸载。' + #13#10 + #13#10 +
           '您的设置和缓存数据保留在：' + #13#10 +
           ExpandConstant('{localappdata}\JASM') + #13#10 + #13#10 +
           '如需完全清除，请手动删除该文件夹。',
           mbInformation, MB_OK);
  end;
end;

// 检测是否传入了 /AUTORUN 参数
function IsAutoRun: Boolean;
var
  i: Integer;
begin
  Result := False;
  for i := 1 to ParamCount do
  begin
    if CompareText(ParamStr(i), '/AUTORUN') = 0 then
    begin
      Result := True;
      Break;
    end;
  end;
end;

// 安装程序以管理员权限运行时，[Run] 中启动的 JASM 会继承提权令牌，
// 导致其启动后误报“以管理员身份运行警告”（GitHub Issue #65，
// Inno Setup 的 runasoriginaluser 在安装程序被“以管理员身份运行”启动时无效）。
// 此时跳过自动启动，让用户从开始菜单/桌面快捷方式以普通权限打开 JASM。
function ShouldLaunchApp: Boolean;
begin
  Result := not IsAdmin;
end;

// 安装程序启动时，智能等待主进程完全退出
function InitializeSetup(): Boolean;
var
  i: Integer;
begin
  // 如果是静默更新模式，循环等待 Mutex 释放（最多等待 10 秒）
  if WizardSilent then
  begin
    for i := 0 to 10 do
    begin
      if not CheckForMutexes('JASM_WinUI_App_Mutex') then
        break;
      Sleep(1000);
    end;
  end;
  Result := True;
end;

procedure InitializeWizard();
begin
  if IsAutoRun then
  begin
    WizardForm.CancelButton.Visible := False;
    WizardForm.Caption := '正在更新 {#MyAppName}...';
  end;
end;
