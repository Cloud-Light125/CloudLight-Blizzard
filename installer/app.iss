#define AppName "CloudLight Blizzard"
#define AppVer  "1.0.0"
#define AppExe  "BnetSwitch.exe"

[Setup]
AppId={{8F3A2B10-9C4D-4E7F-A1B2-3C4D5E6F7A8B}
AppName={#AppName}
AppVersion={#AppVer}
AppVerName={#AppName} {#AppVer}
AppPublisher=CloudLight
DefaultDirName={autopf}\CloudLight\CloudLight Blizzard
DefaultGroupName=CloudLight Blizzard
DisableProgramGroupPage=yes
DisableDirPage=no
AllowNoIcons=yes
OutputDir=out
OutputBaseFilename=CloudLight-Blizzard-{#AppVer}-win-x64-Setup
SetupIconFile=..\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
CloseApplications=yes
CloseApplicationsFilter=BnetSwitch.exe

[Languages]
Name: "chinese"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式:"

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "立即运行 {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDesktopRuntimeInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM64,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App') or
    RegKeyExists(HKLM,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App');
end;

function InitializeSetup: Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDesktopRuntimeInstalled then
  begin
    if MsgBox('CloudLight Blizzard 需要 .NET 8 Windows Desktop Runtime x64。' + #13#10 +
      '当前未检测到该运行库。是否打开微软官方下载页面？',
      mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('', 'https://aka.ms/dotnet/8.0/windowsdesktop-runtime-win-x64.exe',
        '', '', SW_SHOWNORMAL, ewNoWait, ErrorCode);
  end;
end;
