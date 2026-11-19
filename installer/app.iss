#define AppName "CloudLight Blizzard"
#define AppMajor 3
#define AppMinor 0
#define AppPatch 1
#define AppVer Str(AppMajor) + "." + Str(AppMinor) + "." + Str(AppPatch)
#define AppVersionMS AppMajor * 65536 + AppMinor
#define AppVersionLS AppPatch * 65536
#define AppExe  "CloudLight Blizzard.exe"
#ifndef PublishDir
  #define PublishDir "..\publish"
#endif

[Setup]
AppId={{8F3A2B10-9C4D-4E7F-A1B2-3C4D5E6F7A8B}
AppName={#AppName}
AppVersion={#AppVer}
AppVerName={#AppName} {#AppVer}
VersionInfoVersion={#AppVer}.0
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
CloseApplications=no
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

function OpenExistingForWrite(FileName: String; Access, Share: Cardinal;
  Security: Integer; Disposition, Attributes: Cardinal; Template: THandle): THandle;
  external 'CreateFileW@kernel32.dll stdcall';
function CloseFileHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';

function PayloadFileIsBusy(const RelativePath: String): Boolean;
var
  FileName: String;
  Handle: THandle;
begin
  Result := False;
  FileName := ExpandConstant('{app}\') + RelativePath;
  if not FileExists(FileName) then Exit;
  { Opening an existing file for write performs no writes. Windows rejects an
    active executable image, including an independently running Worker. }
  Handle := OpenExistingForWrite(FileName, $40000000, 7, 0, 3, $80, 0);
  if Handle = THandle(-1) then
    Result := True
  else
    CloseFileHandle(Handle);
end;

function IsNewerProductVersion(const VersionMS, VersionLS: Cardinal): Boolean;
begin
  Result := (VersionMS > {#AppVersionMS}) or
    ((VersionMS = {#AppVersionMS}) and (VersionLS > {#AppVersionLS}));
end;

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

function CopyVerifiedPayload(const SourceRoot, DestinationRoot, Relative: String): Boolean;
var
  FindRec: TFindRec;
  SourcePath, DestinationPath, Child: String;
begin
  Result := True;
  SourcePath := AddBackslash(SourceRoot) + Relative;
  DestinationPath := AddBackslash(DestinationRoot) + Relative;
  if not ForceDirectories(DestinationPath) then
  begin
    Result := False;
    Exit;
  end;
  if FindFirst(AddBackslash(SourcePath) + '*', FindRec) then
  begin
    try
      repeat
        if (FindRec.Name <> '.') and (FindRec.Name <> '..') then
        begin
          Child := AddBackslash(Relative) + FindRec.Name;
          if Relative = '' then Child := FindRec.Name;
          if (FindRec.Attributes and FILE_ATTRIBUTE_DIRECTORY) <> 0 then
          begin
            if not CopyVerifiedPayload(SourceRoot, DestinationRoot, Child) then Result := False;
          end
          else
          begin
            Log('PAYLOAD_FILE_SHA256 ' + Child + '=' + Lowercase(GetSHA256OfFile(AddBackslash(SourceRoot) + Child)));
            if not CopyFile(AddBackslash(SourceRoot) + Child, AddBackslash(DestinationRoot) + Child, True) then Result := False;
          end;
        end;
      until not FindNext(FindRec);
    finally
      FindClose(FindRec);
    end;
  end;
end;

function VerifyInstallerPayload: Boolean;
var
  PayloadExeFile: String;
  PayloadFile: String;
  PayloadExeHash: String;
  PayloadHash: String;
  PayloadOutput: String;
  PayloadRoot: String;
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
    PayloadOutput := ExpandConstant('{param:PAYLOADOUT|}');
    if PayloadOutput <> '' then
    begin
      { This mode aborts before installation and only writes a new directory.
        Never overwrite an installed tree, configuration, or prior extraction. }
      if DirExists(PayloadOutput) or FileExists(PayloadOutput) then
        RaiseException('PAYLOADOUT must be a new directory.');
      ExtractTemporaryFiles('{app}\*');
      PayloadRoot := ExpandConstant('{tmp}\') + '{app}';
      if not CopyVerifiedPayload(PayloadRoot, PayloadOutput, '') then
        RaiseException('Payload extraction failed.');
      Log('PAYLOAD_EXTRACT PASS: ' + PayloadOutput);
    end;
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

function VerifyAllRetainedUserFiles: Boolean;
var
  Paths: TArrayOfString;
  I: Integer;
  TestRoot, FileName, Expected: String;
  Actual: AnsiString;
begin
  Result := False;
  TestRoot := ExpandConstant('{tmp}\CloudLight-All-Data-Preserve-SelfTest');
  SetArrayLength(Paths, 13);
  Paths[0] := 'settings.json';
  Paths[1] := 'drops\twitch\settings.json';
  Paths[2] := 'drops\soop\settings.json';
  Paths[3] := 'drops\youtube\config.json';
  Paths[4] := 'drops\bilibili\settings.json';
  Paths[5] := 'drops\twitch\cookies.jar';
  Paths[6] := 'drops\twitch\claim-journal.json';
  Paths[7] := 'drops\soop\accounts\synthetic\cookies.json';
  Paths[8] := 'drops\soop\credentials\synthetic.bin';
  Paths[9] := 'drops\bilibili\state.json';
  Paths[10] := 'accounts\synthetic\manifest.json';
  Paths[11] := 'accounts\synthetic\Battle.net.config';
  Paths[12] := 'region-switch\backups\synthetic.config';
  UserDataRoot := AddBackslash(TestRoot) + 'data';
  SettingsBackupRoot := AddBackslash(TestRoot) + 'backup';
  for I := 0 to GetArrayLength(Paths) - 1 do
  begin
    FileName := AddBackslash(UserDataRoot) + Paths[I];
    if not ForceDirectories(ExtractFileDir(FileName)) then Exit;
    Expected := '{"syntheticOnly":true,"preserved":"' + IntToStr(I) +
      '","campaign_watch_priority":{"1":{"mode":"custom","campaignIds":["b","a"]}},"uncertain":"unconfirmed"}';
    if not SaveStringToFile(FileName, Expected, False) then Exit;
    if I < 5 then
    begin
      if not BackupSettingsFile(Paths[I]) then Exit;
      if not SaveStringToFile(FileName, '{"syntheticOnly":true,"changed":true}', False) then Exit;
    end;
  end;
  SettingsBackupReady := True;
  if not RestoreUserSettings then Exit;
  for I := 0 to GetArrayLength(Paths) - 1 do
  begin
    Expected := '{"syntheticOnly":true,"preserved":"' + IntToStr(I) +
      '","campaign_watch_priority":{"1":{"mode":"custom","campaignIds":["b","a"]}},"uncertain":"unconfirmed"}';
    if not LoadStringFromFile(AddBackslash(UserDataRoot) + Paths[I], Actual) then Exit;
    if Actual <> Expected then Exit;
    Log('ALL_DATA_PRESERVE_TEST PASS: ' + Paths[I]);
  end;
  Log('ALL_DATA_PRESERVE_TEST PASS: 13 isolated synthetic files');
  if not IsNewerProductVersion({#AppVersionMS} + 1, 0) or
     not IsNewerProductVersion({#AppVersionMS}, {#AppVersionLS} + 1) or
     IsNewerProductVersion({#AppVersionMS}, {#AppVersionLS}) or
     IsNewerProductVersion($00030000, 0) then Exit;
  Log('PRODUCT_DOWNGRADE_GUARD_TEST PASS');
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
    if VerifySettingsPreservation and VerifyAllRetainedUserFiles then
      Log('ALL_PRESERVATION_CHECKS PASS');
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
#if FileExists(PublishDir + "\coreclr.dll")
  Result := True;
  Exit;
#endif
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
var
  ExistingMS, ExistingLS: Cardinal;
begin
  Result := '';
  if PayloadVerifyRequested or SettingsPreserveTestRequested then
    Exit;
  if GetVersionNumbers(ExpandConstant('{app}\CloudLight Blizzard.dll'), ExistingMS, ExistingLS) and
     IsNewerProductVersion(ExistingMS, ExistingLS) then
  begin
    Result := '安装目录包含比 {#AppVer} 更新的 CloudLight Blizzard；为避免覆盖新版程序与 Worker，安装已阻止。';
    Exit;
  end;
  if CheckForMutexes('Local\CloudLight Blizzard.SingleInstance') then
  begin
    Result := 'CloudLight Blizzard 仍在运行。请从程序菜单正常退出，等待后台停止后重试安装。';
    Exit;
  end;
  if PayloadFileIsBusy('{#AppExe}') or
     PayloadFileIsBusy('CloudLight Blizzard.dll') or
     PayloadFileIsBusy('_internal\drops\twitch\twitch-worker.exe') or
     PayloadFileIsBusy('_internal\drops\soop\soop-worker.exe') or
     PayloadFileIsBusy('_internal\drops\bilibili\bilibili-worker.exe') or
     PayloadFileIsBusy('_internal\drops\youtube\youtube-worker.exe') then
  begin
    Result := '安装目录的程序或 Worker 仍在使用或不可写。请正常退出相关 CloudLight Blizzard 实例后重试。';
    Exit;
  end;
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
