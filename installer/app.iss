#define AppName "CloudLight Blizzard"
#define AppVer  "2.0.10"
#define AppExe  "CloudLight Blizzard.exe"
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

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
CloseApplicationsFilter=CloudLight Blizzard.exe

[Languages]
Name: "chinese"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "CloudLight.CloudLightBlizzard"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon; AppUserModelID: "CloudLight.CloudLightBlizzard"

[Run]
Filename: "{app}\{#AppExe}"; Description: "立即运行 {#AppName}"; Flags: nowait postinstall skipifsilent

[Code]
function IsDesktopRuntimeInstalled: Boolean;
var
  RuntimeNames: TArrayOfString;
  I: Integer;
  DotPosition: Integer;
  MajorVersion: Integer;
begin
  Result := False;

  { .NET writes x64 shared-framework versions as value names under this key.
    Depending on the installer/runtime combination the registration can be
    visible through either registry view, so check both. }
  if not RegGetValueNames(HKLM32,
    'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
    RuntimeNames) then
    RegGetValueNames(HKLM64,
      'SOFTWARE\dotnet\Setup\InstalledVersions\x64\sharedfx\Microsoft.WindowsDesktop.App',
      RuntimeNames);

  for I := 0 to GetArrayLength(RuntimeNames) - 1 do
  begin
    DotPosition := Pos('.', RuntimeNames[I]);
    if DotPosition > 1 then
      MajorVersion := StrToIntDef(Copy(RuntimeNames[I], 1, DotPosition - 1), 0)
    else
      MajorVersion := 0;
    if MajorVersion >= 8 then
    begin
      Result := True;
      Exit;
    end;
  end;
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

procedure CurStepChanged(CurStep: TSetupStep);
var
  LegacyName: String;
  LegacyCommand: String;
begin
  if CurStep <> ssPostInstall then
    Exit;

  LegacyName := 'Bnet' + 'Switch';
  if RegQueryStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
    LegacyName, LegacyCommand) then
  begin
    RegWriteStringValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run',
      '{#AppName}', '"' + ExpandConstant('{app}\{#AppExe}') + '" --tray');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', LegacyName);
  end;
  DeleteFile(ExpandConstant('{app}\' + LegacyName + '.exe'));
end;
