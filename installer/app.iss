#define AppName "CloudLight Blizzard"
#define AppVer  "2.1.9"
#define AppExe  "CloudLight Blizzard.exe"
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{8F3A2B10-9C4D-4E7F-A1B2-3C4D5E6F7A8B}
AppName={#AppName}
AppVersion={#AppVer}
AppVerName={#AppName} {#AppVer}
VersionInfoVersion=2.1.9.0
VersionInfoProductVersion={#AppVer}
VersionInfoTextVersion={#AppVer}
VersionInfoProductTextVersion={#AppVer}
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
CloseApplications=force
CloseApplicationsFilter=CloudLight Blizzard.exe
RestartApplications=no

[Languages]
Name: "chinese"; MessagesFile: "ChineseSimplified.isl"

[Tasks]
Name: "desktopicon"; Description: "创建桌面快捷方式"; GroupDescription: "附加快捷方式:"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Excludes: "*.pdb"; Flags: ignoreversion recursesubdirs createallsubdirs; AfterInstall: RestoreUserSettingsBeforeLaunch
; Keep a temporary, non-installed copy so build.ps1 can verify the compressed
; installer payload without installing the application or touching shortcuts.
Source: "{#PublishDir}\CloudLight Blizzard.exe"; Flags: dontcopy
Source: "{#PublishDir}\CloudLight Blizzard.dll"; Flags: dontcopy

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"; AppUserModelID: "CloudLight.CloudLightBlizzard"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon; AppUserModelID: "CloudLight.CloudLightBlizzard"

[Run]
; Setup is elevated, but application settings belong to the original signed-in user.
Filename: "{app}\{#AppExe}"; Description: "立即运行 {#AppName}"; Flags: nowait postinstall skipifsilent runasoriginaluser

[Code]
var
  PayloadVerifyRequested: Boolean;
  SettingsPreserveTestRequested: Boolean;
  UserDataRoot: String;
  SettingsBackupRoot: String;
  SettingsBackupReady: Boolean;
  SettingsRestoreAttempted: Boolean;

function BackupSettingsFile(const RelativePath: String): Boolean;
var
  SourceFile: String;
  BackupFile: String;
begin
  Result := True;
  SourceFile := AddBackslash(UserDataRoot) + RelativePath;
  if not FileExists(SourceFile) then
    Exit;

  BackupFile := AddBackslash(SettingsBackupRoot) + RelativePath;
  if not ForceDirectories(ExtractFileDir(BackupFile)) then
  begin
    Log('SETTINGS_BACKUP FAIL create directory: ' + ExtractFileDir(BackupFile));
    Result := False;
    Exit;
  end;
  if not CopyFile(SourceFile, BackupFile, False) then
  begin
    Log('SETTINGS_BACKUP FAIL copy: ' + SourceFile);
    Result := False;
    Exit;
  end;
  Log('SETTINGS_BACKUP PASS: ' + RelativePath);
end;

function BackupUserSettings: Boolean;
var
  Stamp: String;
begin
  UserDataRoot := ExpandConstant('{userdocs}\CloudLight\CloudLight Blizzard');
  { GetDateTimeString requires Char separators. Empty strings compile but fail
    at runtime with "Type Mismatch" before any snapshot can be created. }
  Stamp := GetDateTimeString('yyyymmdd-hhnnss', '-', ':');
  SettingsBackupRoot := AddBackslash(UserDataRoot) +
    'update-backups\preinstall-{#AppVer}-' + Stamp;
  Result := True;
  if not BackupSettingsFile('settings.json') then Result := False;
  if not BackupSettingsFile('drops\soop\settings.json') then Result := False;
  if not BackupSettingsFile('drops\twitch\settings.json') then Result := False;
  if not BackupSettingsFile('drops\youtube\config.json') then Result := False;
  if not BackupSettingsFile('drops\bilibili\settings.json') then Result := False;
  SettingsBackupReady := Result;
  if Result then
    Log('SETTINGS_BACKUP COMPLETE: ' + SettingsBackupRoot)
  else
    Log('SETTINGS_BACKUP INCOMPLETE');
end;

function RestoreSettingsFile(const RelativePath: String): Boolean;
var
  SourceFile: String;
  DestinationFile: String;
begin
  Result := True;
  SourceFile := AddBackslash(SettingsBackupRoot) + RelativePath;
  if not FileExists(SourceFile) then
    Exit;

  DestinationFile := AddBackslash(UserDataRoot) + RelativePath;
  if not ForceDirectories(ExtractFileDir(DestinationFile)) then
  begin
    Log('SETTINGS_RESTORE FAIL create directory: ' + ExtractFileDir(DestinationFile));
    Result := False;
    Exit;
  end;
  if not CopyFile(SourceFile, DestinationFile, False) then
  begin
    Log('SETTINGS_RESTORE FAIL copy: ' + DestinationFile);
    Result := False;
    Exit;
  end;
  Log('SETTINGS_RESTORE PASS: ' + RelativePath);
end;

function RestoreUserSettings: Boolean;
begin
  if not SettingsBackupReady then
  begin
    Result := True;
    Exit;
  end;
  Result := True;
  if not RestoreSettingsFile('settings.json') then Result := False;
  if not RestoreSettingsFile('drops\soop\settings.json') then Result := False;
  if not RestoreSettingsFile('drops\twitch\settings.json') then Result := False;
  if not RestoreSettingsFile('drops\youtube\config.json') then Result := False;
  if not RestoreSettingsFile('drops\bilibili\settings.json') then Result := False;
  if Result then
    Log('SETTINGS_RESTORE COMPLETE: ' + SettingsBackupRoot)
  else
    Log('SETTINGS_RESTORE INCOMPLETE; recovery snapshot retained at: ' + SettingsBackupRoot);
end;

procedure RestoreUserSettingsBeforeLaunch;
begin
  if SettingsRestoreAttempted then
    Exit;
  SettingsRestoreAttempted := True;
  if not RestoreUserSettings then
    RaiseException('无法恢复安装前设置。恢复快照位于：' + SettingsBackupRoot);
end;

function HasCommandLineParam(const Name: String): Boolean;
var
  I: Integer;
begin
  Result := False;
  for I := 1 to ParamCount do
    if CompareText(ParamStr(I), Name) = 0 then
    begin
      Result := True;
      Exit;
    end;
end;

function VerifyInstallerPayload: Boolean;
var
  PayloadExeFile: String;
  PayloadFile: String;
  PayloadExeHash: String;
  PayloadHash: String;
begin
  Result := False;
  try
    ExtractTemporaryFile('CloudLight Blizzard.exe');
    ExtractTemporaryFile('CloudLight Blizzard.dll');
    PayloadExeFile := ExpandConstant('{tmp}\CloudLight Blizzard.exe');
    PayloadFile := ExpandConstant('{tmp}\CloudLight Blizzard.dll');
    if (not FileExists(PayloadExeFile)) or (not FileExists(PayloadFile)) then
    begin
      Log('PAYLOAD_VERIFY FAIL missing extracted CloudLight Blizzard.exe or CloudLight Blizzard.dll');
      Exit;
    end;
    PayloadExeHash := Lowercase(GetSHA256OfFile(PayloadExeFile));
    PayloadHash := Lowercase(GetSHA256OfFile(PayloadFile));
    Log('PAYLOAD_VERIFY EXE_SHA256=' + PayloadExeHash);
    Log('PAYLOAD_VERIFY DLL_SHA256=' + PayloadHash);
    Log('PAYLOAD_VERIFY PASS');
    Result := True;
  except
    Log('PAYLOAD_VERIFY FAIL exception');
  end;
end;

function VerifySettingsPreservation: Boolean;
var
  TestRoot: String;
  TestDataRoot: String;
  TestBackupRoot: String;
  TestSettingsFile: String;
  RestoredContents: AnsiString;
begin
  Result := False;
  TestRoot := ExpandConstant('{tmp}\CloudLight-Settings-Preserve-SelfTest');
  TestDataRoot := AddBackslash(TestRoot) + 'data';
  TestBackupRoot := AddBackslash(TestRoot) + 'backup-' +
    GetDateTimeString('yyyymmdd-hhnnss', '-', ':');
  TestSettingsFile := AddBackslash(TestDataRoot) + 'settings.json';

  if not ForceDirectories(TestDataRoot) then
  begin
    Log('SETTINGS_PRESERVE_TEST FAIL create test directory');
    Exit;
  end;
  if not SaveStringToFile(TestSettingsFile, '{"proxy":"before-update"}', False) then
  begin
    Log('SETTINGS_PRESERVE_TEST FAIL create source settings');
    Exit;
  end;

  UserDataRoot := TestDataRoot;
  SettingsBackupRoot := TestBackupRoot;
  SettingsBackupReady := BackupSettingsFile('settings.json');
  if not SettingsBackupReady then
  begin
    Log('SETTINGS_PRESERVE_TEST FAIL backup');
    Exit;
  end;
  if not SaveStringToFile(TestSettingsFile, '{"proxy":"installer-default"}', False) then
  begin
    Log('SETTINGS_PRESERVE_TEST FAIL mutate source settings');
    Exit;
  end;
  if not RestoreUserSettings then
  begin
    Log('SETTINGS_PRESERVE_TEST FAIL restore');
    Exit;
  end;
  if not LoadStringFromFile(TestSettingsFile, RestoredContents) then
  begin
    Log('SETTINGS_PRESERVE_TEST FAIL read restored settings');
    Exit;
  end;
  if RestoredContents <> '{"proxy":"before-update"}' then
  begin
    Log('SETTINGS_PRESERVE_TEST FAIL restored contents mismatch');
    Exit;
  end;

  Log('SETTINGS_PRESERVE_TEST PASS');
  Result := True;
end;

procedure InitializeWizard;
begin
  if PayloadVerifyRequested then
  begin
    VerifyInstallerPayload;
    Abort;
  end;
  if SettingsPreserveTestRequested then
  begin
    VerifySettingsPreservation;
    Abort;
  end;
end;

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
  PayloadVerifyRequested := HasCommandLineParam('/PAYLOADVERIFY');
  SettingsPreserveTestRequested := HasCommandLineParam('/SETTINGSPRESERVETEST');
  if PayloadVerifyRequested or SettingsPreserveTestRequested then
  begin
    Result := True;
    Exit;
  end;

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

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  Result := '';
  if PayloadVerifyRequested or SettingsPreserveTestRequested then
    Exit;
  if not BackupUserSettings then
    Result := '无法在安装前安全备份 CloudLight Blizzard 设置。' + #13#10 +
      '为避免设置丢失，本次安装已取消。';
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
