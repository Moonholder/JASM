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
; VC++ Redistributable（安装后删除临时文件）
Source: "redist\vc_redist.x64.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not VCRedistInstalled

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
; 安装 VC++ Redistributable（仅在未安装时）
Filename: "{tmp}\vc_redist.x64.exe"; Parameters: "/install /quiet /norestart"; StatusMsg: "正在安装 Visual C++ 运行库..."; Check: not VCRedistInstalled; Flags: waituntilterminated
; 手动安装模式下提供启动复选框
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; WorkingDir: "{app}"; Flags: nowait postinstall skipifsilent shellexec
; 静默安装完成后的强制自启动
Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Flags: nowait shellexec; Check: IsAutoRun

[UninstallDelete]
; 卸载时清理可能生成的运行时文件，但不删除用户数据（%LOCALAPPDATA%\JASM 中的设置）
Type: files; Name: "{app}\*.log"
Type: dirifempty; Name: "{app}"

[Code]
// 检查 VC++ Redistributable 2015-2022 (x64) 是否已安装
function VCRedistInstalled: Boolean;
var
  Version: String;
begin
  Result := RegQueryStringValue(HKLM,
    'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64',
    'Version', Version);
  if Result then
    Log('VC++ Redistributable found: ' + Version)
  else
    Log('VC++ Redistributable not found, will install');
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
