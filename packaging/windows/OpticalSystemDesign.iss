; Compile via scripts/build-installer.ps1. No third-party executable is fetched by Setup.
#if Ver < EncodeVer(6, 3, 0)
  #error Inno Setup 6.3 or later is required
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef TargetRuntime
  #error TargetRuntime is required
#endif
#ifndef ReleaseId
  #error ReleaseId is required
#endif
#ifndef ManifestHash
  #error ManifestHash is required
#endif
#ifndef ProductAppId
  #define ProductAppId "OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A"
#endif
#if Len(ProductAppId) > 56
  #error ProductAppId must remain within the production identity length to preserve exact registry lookup
#endif
#ifndef AppName
  #define AppName "Optical System Design"
#endif
#define AppExe "OpticalSystemDesign.App.exe"
#define PayloadDir "versions\" + ReleaseId
#define ApplicationMutex "Local\" + ProductAppId + "-Application"
#if ProductAppId == "OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A"
  #define AssociationId "STARLabs.OpticalSystemDesign.Project"
#else
  #define AssociationId ProductAppId + ".Project"
#endif

[Setup]
AppId={#ProductAppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=S.T.A.R. Labs
DefaultDirName={localappdata}\Programs\S.T.A.R. Labs\Optical System Design
DefaultGroupName=S.T.A.R. Labs\Optical System Design
PrivilegesRequired=lowest
WizardStyle=modern
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=no
AllowNoIcons=yes
UsePreviousAppDir=yes
UsePreviousGroup=yes
UsePreviousTasks=yes
MinVersion=10.0.19045
#if TargetRuntime == "win-arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#elif TargetRuntime == "win-x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
  #error Unsupported TargetRuntime
#endif
SetupIconFile={#PublishDir}\Assets\Brand\AppIcon.ico
UninstallDisplayIcon={app}\{#PayloadDir}\{#AppExe}
UninstallDisplayName={#AppName}
VersionInfoDescription=Optical System Design Setup
Compression=none
SolidCompression=no
CloseApplications=no
RestartApplications=no
AppMutex={#ApplicationMutex}
SetupMutex={#ApplicationMutex}-Setup
#ifdef TrustedSigning
SignTool=OpticalPublisher
SignedUninstaller=yes
#else
SignedUninstaller=no
#endif
Uninstallable=yes
SetupLogging=yes
OutputBaseFilename=OpticalSystemDesign-{#AppVersion}-{#TargetRuntime}-Setup

[Languages]
Name: "chinesesimp"; MessagesFile: "inno\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\OpticalSystemDesign.Application.dll"; DestDir: "{app}\{#PayloadDir}"; Flags: ignoreversion; Check: NeedsPayload; AfterInstall: AfterPartialPayloadCopy
Source: "{#PublishDir}\*"; DestDir: "{app}\{#PayloadDir}"; Excludes: "release.files.sha256,OpticalSystemDesign.Application.dll"; Flags: ignoreversion recursesubdirs createallsubdirs; Check: NeedsPayload
Source: "{#PublishDir}\release.files.sha256"; DestDir: "{app}\{#PayloadDir}"; Flags: ignoreversion; Check: NeedsPayload; AfterInstall: VerifyPayload
Source: "inno\LICENSE.txt"; DestDir: "{app}\ThirdParty\InnoSetup"; Flags: ignoreversion
Source: "inno\SOURCE.md"; DestDir: "{app}\ThirdParty\InnoSetup"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#PayloadDir}\{#AppExe}"; WorkingDir: "{app}\{#PayloadDir}"; Check: PayloadVerified
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"; Check: PayloadVerified
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#PayloadDir}\{#AppExe}"; WorkingDir: "{app}\{#PayloadDir}"; Tasks: desktopicon; Check: PayloadVerified

[Run]
Filename: "{app}\{#PayloadDir}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent unchecked; Check: PayloadVerified

; Inno's uninstall log owns only installed files and shortcuts. Do not add
; recursive InstallDelete/UninstallDelete entries or delete user settings/projects.

[Code]
var
  Verified, ExistingPayload, Captured, Committed, StateChanged: Boolean;
  PreviousStateExists: Boolean;
  PreviousState: AnsiString;
  PreviousDisplayVersion: String;
  PreviousDisplayVersionExists: Boolean;
  ShortcutPaths, ShortcutCopies: array[0..2] of String;
  ShortcutExisted: array[0..2] of Boolean;
  UninstallGate: THandle;
  InstallationFileGate: THandle;
  InstallationGateRoot: String;
  Failed: Boolean;
  FailureReason: String;
  AssociationOwned, AssociationChanged, PreviousIconExists: Boolean;
  PreviousCommand, PreviousIcon: String;
  HasRegisteredInstall, RegisteredNoIcons, RegisteredDesktopIcon: Boolean;
  RegisteredDirectory, RegisteredGroup, RegisteredVersion, InstallationMode: String;

function MoveFileExW(ExistingFile, NewFile: String; Flags: Cardinal): Boolean;
  external 'MoveFileExW@kernel32.dll stdcall';
function GetFileAttributesW(Name: String): Cardinal;
  external 'GetFileAttributesW@kernel32.dll stdcall';
function CreateFileW(Name: String; Access, Share: Cardinal; Security: Integer;
  Creation, Flags: Cardinal; Template: Integer): THandle;
  external 'CreateFileW@kernel32.dll stdcall';
function CloseHandle(Handle: THandle): Boolean;
  external 'CloseHandle@kernel32.dll stdcall';
function CreateMutexW(Security: Integer; Owned: Boolean; Name: String): THandle;
  external 'CreateMutexW@kernel32.dll stdcall';
function FlushFileBuffers(Handle: THandle): Boolean;
  external 'FlushFileBuffers@kernel32.dll stdcall';
function WritePrivateProfileStringW(Section, Key, Value: Integer; FileName: String): Boolean;
  external 'WritePrivateProfileStringW@kernel32.dll stdcall';
function WriteFile(Handle: THandle; Buffer: String; BytesToWrite: Cardinal; var BytesWritten: Cardinal; Overlapped: Integer): Boolean;
  external 'WriteFile@kernel32.dll stdcall';
#ifdef ValidationHooks
function GetCurrentProcessId: Cardinal;
  external 'GetCurrentProcessId@kernel32.dll stdcall';
#endif

function StatePath: String;
begin
  Result := ExpandConstant('{app}\release-state.ini');
end;

function SafeRelativeName(Name: String): Boolean;
begin
  Result := (Name <> '') and (Pos('..', Name) = 0) and
    (Pos(':', Name) = 0) and (Copy(Name, 1, 1) <> '\') and
    (Copy(Name, 1, 1) <> '/') and (Pos(#13, Name) = 0) and (Pos(#10, Name) = 0);
end;

procedure CheckNoReparsePath(Name: String);
var Parent: String;
begin
  if (GetFileAttributesW(Name) <> $FFFFFFFF) and
    ((GetFileAttributesW(Name) and $400) <> 0) then
    RaiseException('安装路径不能包含符号链接或目录联接：' + Name);
  Parent := ExtractFileDir(Name);
  if (Parent <> '') and (Parent <> Name) then CheckNoReparsePath(Parent);
end;

function ExclusiveFileAvailable(Name: String): Boolean;
var Handle: THandle;
begin
  Result := True;
  if not FileExists(Name) then Exit;
  Handle := CreateFileW(Name, $C0000000, 0, 0, 3, 0, 0);
  Result := Handle <> THandle(-1);
  if Result then CloseHandle(Handle);
end;

procedure ReleaseInstallationFileGate;
begin
  if InstallationFileGate <> 0 then CloseHandle(InstallationFileGate);
  InstallationFileGate := 0;
  InstallationGateRoot := '';
end;

procedure AcquireInstallationFileGate;
var Root, Name: String; Application: THandle;
begin
  Root := ExpandConstant('{app}');
  if (InstallationFileGate <> 0) and (CompareText(Root, InstallationGateRoot) = 0) then Exit;
  ReleaseInstallationFileGate;
  CheckNoReparsePath(Root);
  if not ForceDirectories(Root) then RaiseException('无法访问安装目录。');
  Name := Root + '\.setup.lock';
  CheckNoReparsePath(Name);
  InstallationFileGate := CreateFileW(Name, $C0000000, 0, 0, 4, 0, 0);
  if InstallationFileGate = THandle(-1) then begin
    InstallationFileGate := 0;
    RaiseException('此安装目录有安装、卸载或维护任务运行，可能位于另一个登录会话。');
  end;
  try
    Name := Root + '\.application.lock';
    CheckNoReparsePath(Name);
    Application := CreateFileW(Name, $C0000000, 0, 0, 4, 0, 0);
    if Application = THandle(-1) then
      RaiseException('此安装目录的程序仍在运行，可能位于另一个登录会话。请保存并正常退出。');
    CloseHandle(Application);
    InstallationGateRoot := Root;
  except
    ReleaseInstallationFileGate;
    RaiseException(GetExceptionMessage);
  end;
end;

function CompareVersions(Left, Right: String): Integer;
var I, L, R, P: Integer; Part: String;
begin
  Result := 0;
  for I := 0 to 3 do begin
    P := Pos('.', Left); if P = 0 then P := Length(Left) + 1;
    Part := Copy(Left, 1, P - 1); Delete(Left, 1, P);
    L := StrToIntDef(Part, 0);
    P := Pos('.', Right); if P = 0 then P := Length(Right) + 1;
    Part := Copy(Right, 1, P - 1); Delete(Right, 1, P);
    R := StrToIntDef(Part, 0);
    if L < R then begin Result := -1; Exit; end;
    if L > R then begin Result := 1; Exit; end;
  end;
end;

procedure ValidateFiles;
var Rows: TArrayOfString; I: Integer; Manifest, Relative, Target: String;
begin
  Manifest := ExpandConstant('{app}\{#PayloadDir}\release.files.sha256');
  CheckNoReparsePath(Manifest);
  if GetSHA256OfFile(Manifest) <> '{#ManifestHash}' then
    RaiseException('安装文件清单校验失败；旧版本未被覆盖。');
  if not LoadStringsFromFile(Manifest, Rows) then RaiseException('无法读取安装清单。');
  if (GetArrayLength(Rows) = 0) or (GetArrayLength(Rows) > 10000) then
    RaiseException('安装清单的文件数量无效。');
  for I := 0 to GetArrayLength(Rows) - 1 do begin
    if (Length(Rows[I]) < 67) or (Copy(Rows[I], 65, 2) <> '  ') then
      RaiseException('安装清单格式错误。');
    Relative := Copy(Rows[I], 67, Length(Rows[I]));
    if not SafeRelativeName(Relative) then RaiseException('安装清单路径无效。');
    Target := ExpandConstant('{app}\{#PayloadDir}\') + Relative;
    CheckNoReparsePath(Target);
    if not FileExists(Target) or (GetSHA256OfFile(Target) <> Copy(Rows[I], 1, 64)) then
      RaiseException('安装文件校验失败：' + Relative);
  end;
  Verified := True;
end;

procedure VerifyPayload;
begin
  try
    ValidateFiles;
#ifdef ValidationHooks
    if ExpandConstant('{param:TESTFAILBEFOREACTIVATION|0}') = '1' then
      RaiseException('受控测试：载荷校验后、版本切换前失败。');
#endif
  except
    FailureReason := GetExceptionMessage;
    Failed := True;
    Verified := False;
    Log('FAILURE: ' + FailureReason);
  end;
end;

function NeedsPayload: Boolean;
begin Result := not ExistingPayload; end;
function PayloadVerified: Boolean;
begin Result := Verified and not Failed; end;

procedure CapturePreviousState;
var I: Integer; Key: String;
begin
  if Captured then Exit;
  PreviousStateExists := FileExists(StatePath);
  if PreviousStateExists and not LoadStringFromFile(StatePath, PreviousState) then
    RaiseException('无法保存旧版本状态；安装未开始。');
  ShortcutPaths[0] := ExpandConstant('{group}\{#AppName}.lnk');
  ShortcutPaths[1] := ExpandConstant('{group}\') + ExpandConstant('{cm:UninstallProgram,{#AppName}}') + '.lnk';
  if WizardIsTaskSelected('desktopicon') then
    ShortcutPaths[2] := ExpandConstant('{autodesktop}\{#AppName}.lnk');
  for I := 0 to 2 do begin
    ShortcutExisted[I] := (ShortcutPaths[I] <> '') and FileExists(ShortcutPaths[I]);
    ShortcutCopies[I] := ExpandConstant('{tmp}\previous-shortcut-') + IntToStr(I) + '.lnk';
    if ShortcutExisted[I] and not FileCopy(ShortcutPaths[I], ShortcutCopies[I], False) then
      RaiseException('无法备份旧入口；安装未开始。');
  end;
  Key := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#ProductAppId}_is1';
  PreviousDisplayVersionExists := RegQueryStringValue(HKCU, Key, 'DisplayVersion', PreviousDisplayVersion);
  Captured := True;
end;

#include "InstallationRecovery.iss"
#include "InstallationPolicy.iss"

// An external manifest survives interrupted cleanup. Publish its receipt last,
// only after a committed, verified installation; never infer ownership of old
// directories that were installed before this protocol existed.
procedure RecordInstalledRelease;
var Directory, Receipt, Temporary, Manifest: String;
begin
  Directory := ExpandConstant('{app}\.installed-releases');
  CheckNoReparsePath(Directory);
  if not ForceDirectories(Directory) then RaiseException('无法创建载荷归属目录。');
  Receipt := Directory + '\{#ReleaseId}.ini';
  Temporary := Receipt + '.tmp';
  Manifest := Directory + '\{#ReleaseId}.sha256';
  CheckNoReparsePath(Receipt); CheckNoReparsePath(Temporary); CheckNoReparsePath(Manifest);
  if not FileCopy(ExpandConstant('{app}\{#PayloadDir}\release.files.sha256'), Manifest, False) then
    RaiseException('无法保存载荷归属清单。');
  FlushDurableFile(Manifest);
  if not SetIniString('Release', 'Format', '1', Temporary) or
    not SetIniString('Release', 'ProductAppId', '{#ProductAppId}', Temporary) or
    not SetIniString('Release', 'Id', '{#ReleaseId}', Temporary) or
    not SetIniString('Release', 'ManifestSha256', '{#ManifestHash}', Temporary) then
    RaiseException('无法保存载荷归属记录。');
  WritePrivateProfileStringW(0, 0, 0, Temporary);
  FlushDurableFile(Temporary);
  if not MoveFileExW(Temporary, Receipt, 9) then RaiseException('无法发布载荷归属记录。');
end;

procedure AfterPartialPayloadCopy;
begin
#ifdef ValidationHooks
  PauseForInterruptionTest('TESTPAUSEAFTERPARTIALCOPY');
#endif
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var PreviousRelease, PreviousVersion, PreviousExe, CommandKey: String; PayloadEntries: Integer;
begin
  Result := '';
  try
    EnforceInstallationIdentity;
    CheckNoReparsePath(ExpandConstant('{app}'));
    CheckNoReparsePath(StatePath);
    AcquireInstallationFileGate;
    if CheckForMutexes('{#ApplicationMutex}') then
      RaiseException('程序仍在运行，请保存工作并正常退出后再安装。安装器不会强制关闭窗口。');
    RecoverInterruptedTransaction;
    // Recovery may restore an older registry/state pair. Decide only after replay,
    // under the installation lock, and before copying or capturing a new transaction.
    ValidateInstallationPolicy;
    PreviousExe := ExpandConstant('{app}\{#AppExe}');
    if FileExists(StatePath) then begin
      if GetIniString('Release', 'ProductAppId', '', StatePath) <> '{#ProductAppId}' then
        RaiseException('目标目录属于其他产品或包含未知安装状态。');
      PreviousRelease := GetIniString('Release', 'Active', '', StatePath);
      if not SafeRelativeName(PreviousRelease) or (Pos('\', PreviousRelease) <> 0) or (Pos('/', PreviousRelease) <> 0) then
        RaiseException('旧安装状态无效。');
      PreviousVersion := GetIniString('Release', 'Version', '', StatePath);
      if PreviousVersion = '' then RaiseException('旧安装版本号缺失。');
      if CompareVersions('{#AppVersion}', PreviousVersion) < 0 then
        RaiseException('拒绝降级安装。请使用同版本或更新版本，旧工程和设置保持不变。');
      PreviousExe := ExpandConstant('{app}\versions\') + PreviousRelease + '\{#AppExe}';
    end;
    if not ExclusiveFileAvailable(PreviousExe) then
      RaiseException('旧程序或其文件正在使用中，请正常退出后再试。');
    CheckNoReparsePath(PreviousExe);
    CheckNoReparsePath(ExpandConstant('{app}\{#PayloadDir}'));
    ExistingPayload := DirExists(ExpandConstant('{app}\{#PayloadDir}'));
    if ExistingPayload then begin
      PayloadEntries := 0;
      CheckPayloadTree(ExpandConstant('{app}\{#PayloadDir}'), 0, PayloadEntries);
      try ValidateFiles;
      except
        Log('Repairing an incomplete or damaged payload from this exact package.');
        ExistingPayload := False;
        Verified := False;
      end;
    end;
    CommandKey := 'Software\Classes\{#AssociationId}\shell\open\command';
    AssociationOwned := RegQueryStringValue(HKCU, CommandKey, '', PreviousCommand) and
      (CompareText(PreviousCommand, '"' + PreviousExe + '" "%1"') = 0);
    if AssociationOwned then
      PreviousIconExists := RegQueryStringValue(HKCU,
        'Software\Classes\{#AssociationId}\DefaultIcon', '', PreviousIcon);
    CapturePreviousState;
    CaptureDurableJournal;
  except
    Result := GetExceptionMessage;
    ReleaseInstallationFileGate;
  end;
end;

procedure ActivateRelease;
var Temporary: String;
begin
  if not Verified then RaiseException('载荷尚未通过校验，不能切换版本。');
  if CheckForMutexes('{#ApplicationMutex}') then RaiseException('程序已启动，取消版本切换。');
  if (GetIniString('Release', 'Active', '', StatePath) = '{#ReleaseId}') and
    (GetIniString('Release', 'Version', '', StatePath) = '{#AppVersion}') then Exit;
  Temporary := StatePath + '.{#ReleaseId}.tmp';
  if not SetIniString('Release', 'ProductAppId', '{#ProductAppId}', Temporary) or
    not SetIniString('Release', 'Active', '{#ReleaseId}', Temporary) or
    not SetIniString('Release', 'Version', '{#AppVersion}', Temporary) or
    not SetIniString('Release', 'Previous', GetIniString('Release', 'Active', '', StatePath), Temporary) then
    RaiseException('无法写入新版本状态，旧版本仍保留。');
  if not MoveFileExW(Temporary, StatePath, 9) then
    RaiseException('无法原子切换新版本状态，旧版本仍保留。');
  StateChanged := True;
end;

procedure UpdateOwnedAssociation;
begin
  if not AssociationOwned then Exit;
  AssociationChanged := True;
  if not RegWriteStringValue(HKCU, 'Software\Classes\{#AssociationId}\shell\open\command', '',
    '"' + ExpandConstant('{app}\{#PayloadDir}\{#AppExe}') + '" "%1"') or
    not RegWriteStringValue(HKCU, 'Software\Classes\{#AssociationId}\DefaultIcon', '',
    '"' + ExpandConstant('{app}\{#PayloadDir}\Assets\Brand\AppIcon.ico') + '",0') then
    RaiseException('无法更新已有工程打开入口，将恢复旧入口。');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then begin
    if CheckForMutexes('{#ApplicationMutex}') then RaiseException('程序仍在运行，安装取消。');
#ifdef ValidationHooks
    PauseForInterruptionTest('TESTPAUSEBEFORECOPY');
#endif
  end;
  if CurStep = ssPostInstall then begin
    if Failed then Exit;
    try
      WriteJournalPhase('Activating');
      ActivateRelease;
      UpdateOwnedAssociation;
#ifdef ValidationHooks
      PauseForInterruptionTest('TESTPAUSEAFTERACTIVATION');
      if ExpandConstant('{param:TESTFAILAFTERACTIVATION|0}') = '1' then
        RaiseException('受控测试：版本切换后失败，恢复旧入口。');
#endif
    except
      FailureReason := GetExceptionMessage;
      Failed := True;
      Verified := False;
      Log('FAILURE: ' + FailureReason);
    end;
  end;
  if CurStep = ssDone then begin
    Committed := not Failed;
    if Committed then begin
      try
        WriteJournalPhase('Committed');
        if not DeleteFile(JournalPath) then Log('Committed journal retained for next startup.');
      except
        Committed := False; Failed := True; FailureReason := GetExceptionMessage;
      end;
      if Committed then begin
        try RecordInstalledRelease;
        except Log('Cleanup receipt unavailable; this payload will be retained: ' + GetExceptionMessage); end;
      end;
    end;
  end;
end;

function GetCustomSetupExitCode: Integer;
begin
  if Failed then Result := 70 else Result := 0;
end;

procedure CurPageChanged(CurPageID: Integer);
begin
  UpdateMaintenancePage(CurPageID);
  if (CurPageID = wpFinished) and Failed then begin
    WizardForm.FinishedHeadingLabel.Caption := '安装未完成';
    WizardForm.FinishedLabel.Caption := FailureReason + #13#10 +
      '旧程序和用户工程保留；关闭安装器后恢复旧版本入口。请查看安装日志。';
  end;
end;

procedure DeinitializeSetup;
begin
  try
    if Captured and not Committed then begin
      try
        AcquireInstallationFileGate;
        RecoverInterruptedTransaction;
      except Log('WARNING: durable rollback incomplete; journal retained: ' + GetExceptionMessage); end;
    end;
  finally ReleaseInstallationFileGate; end;
end;

function InitializeUninstall: Boolean;
var AlreadyExists: Boolean;
begin
  UninstallGate := CreateMutexW(0, False, '{#ApplicationMutex}-Setup');
  AlreadyExists := DLLGetLastError = 183;
  Result := (UninstallGate <> 0) and not AlreadyExists and
    not CheckForMutexes('{#ApplicationMutex}') and not FileExists(JournalPath);
  if Result then begin
    try AcquireInstallationFileGate;
    except Result := False; Log(GetExceptionMessage); end;
  end;
  if not Result then begin
    if UninstallGate <> 0 then CloseHandle(UninstallGate);
    UninstallGate := 0;
    if not UninstallSilent then
      MsgBox('程序、安装任务仍在运行，或上次安装尚待恢复。请正常退出；如有中断安装，请重新运行安装包完成恢复后再卸载。', mbError, MB_OK);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    if GetIniString('Release', 'ProductAppId', '', StatePath) = '{#ProductAppId}' then
      DeleteFile(StatePath);
end;

procedure DeinitializeUninstall;
begin
  ReleaseInstallationFileGate;
  if UninstallGate <> 0 then CloseHandle(UninstallGate);
end;
