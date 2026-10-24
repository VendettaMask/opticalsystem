// One current-user product registration, one directory and one shortcut configuration.
// UsePrevious* only supplies defaults; never treat those defaults as an update policy.
function PolicyText(Chinese, English: String): String;
begin
  if ActiveLanguage = 'english' then Result := English else Result := Chinese;
end;

function CanonicalInstallDirectory(Name: String): String;
begin
  Name := Trim(Name);
  StringChangeEx(Name, '/', '\', True);
  if (Name = '') or (ExtractFileDrive(Name) = '') or
    ((Copy(Name, 1, 2) <> '\\') and (Copy(Name, 2, 2) <> ':\')) or
    (Pos(#13, Name) <> 0) or (Pos(#10, Name) <> 0) then
    RaiseException(PolicyText('已有安装的目录记录无效；为避免重复安装，已停止。',
      'The existing installation path is invalid. Setup stopped to prevent a duplicate installation.'));
  Result := RemoveBackslashUnlessRoot(ExpandFileName(Name));
end;

procedure ReadInstallationRegistration;
var Key, Location, Tasks, NoIcons: String; NoIconsNumber: Cardinal;
begin
  Key := UninstallKey;
  // This package is deliberately current-user only. Never silently switch scope
  // or create an HKCU copy alongside a machine-wide installation of the same AppId.
  if RegKeyExists(HKLM32, Key) or (IsWin64 and RegKeyExists(HKLM64, Key)) then
    RaiseException(PolicyText('检测到同产品的全用户安装。本包不会另建当前用户副本；请使用相同安装范围的维护方式。旧安装和用户数据未修改。',
      'A machine-wide installation of this product exists. This per-user package will not create another copy. Maintain the existing installation in its original scope; no existing data was changed.'));
  HasRegisteredInstall := RegKeyExists(HKCU, Key);
  RegisteredDirectory := ''; RegisteredGroup := ''; RegisteredVersion := '';
  RegisteredNoIcons := False; RegisteredDesktopIcon := False;
  if not HasRegisteredInstall then Exit;
  RegQueryStringValue(HKCU, Key, 'Inno Setup: App Path', RegisteredDirectory);
  Location := '';
  RegQueryStringValue(HKCU, Key, 'InstallLocation', Location);
  if RegisteredDirectory = '' then RegisteredDirectory := Location;
  RegisteredDirectory := CanonicalInstallDirectory(RegisteredDirectory);
  if (Location <> '') and (CompareText(CanonicalInstallDirectory(Location), RegisteredDirectory) <> 0) then
    RaiseException(PolicyText('已有安装的目录记录不一致，不能安全确定更新位置。请保留原目录并检查安装记录。',
      'The existing installation records disagree about its directory. Setup cannot safely choose an update location. Keep the existing directory and check its registration.'));
  if not RegQueryStringValue(HKCU, Key, 'DisplayVersion', RegisteredVersion) or (RegisteredVersion = '') then
    RaiseException(PolicyText('已有安装缺少版本记录，不能安全判断升级或降级。',
      'The existing installation has no version record; Setup cannot safely determine upgrade or downgrade.'));
  NoIcons := '';
  RegQueryStringValue(HKCU, Key, 'Inno Setup: Icon Group', RegisteredGroup);
  RegQueryStringValue(HKCU, Key, 'Inno Setup: No Icons', NoIcons);
  RegisteredNoIcons := (NoIcons = '1') or (CompareText(NoIcons, 'yes') = 0) or (CompareText(NoIcons, 'true') = 0);
  if RegQueryDWordValue(HKCU, Key, 'Inno Setup: No Icons', NoIconsNumber) then RegisteredNoIcons := NoIconsNumber <> 0;
  // Inno stores this marker only when No Icons was checked. An empty group also
  // represents no Start Menu entries in older installations.
  if RegisteredGroup = '' then RegisteredNoIcons := True;
  if not RegisteredNoIcons and (not SafeRelativeName(RegisteredGroup) or
    (Pos('/', RegisteredGroup) <> 0)) then
    RaiseException(PolicyText('已有快捷方式目录记录无效，安装未开始。',
      'The existing shortcut folder record is invalid. Installation has not started.'));
  Tasks := '';
  RegQueryStringValue(HKCU, Key, 'Inno Setup: Selected Tasks', Tasks);
  RegisteredDesktopIcon := Pos(',desktopicon,', ',' + LowerCase(Tasks) + ',') <> 0;
end;

procedure SelectInstallationMode(Directory: String);
var StateVersion, Release, StateFile: String; Target, Previous: Int64;
begin
  InstallationMode := 'Install';
  StateFile := AddBackslash(Directory) + 'release-state.ini';
  if not StrToVersion('{#AppVersion}', Target) then RaiseException('Invalid package version.');
  if HasRegisteredInstall then begin
    if not StrToVersion(RegisteredVersion, Previous) then
      RaiseException(PolicyText('已有安装版本号无效，已停止以防绕过降级保护。',
        'The installed version is invalid. Setup stopped to preserve downgrade protection.'));
    if ComparePackedVersion(Target, Previous) < 0 then
      RaiseException(PolicyText('拒绝降级安装。请使用同版本或更新版本，旧工程和设置保持不变。',
        'Downgrade refused. Use the same or a newer version; existing projects and settings are unchanged.'));
    if ComparePackedVersion(Target, Previous) = 0 then InstallationMode := 'Repair'
    else InstallationMode := 'Upgrade';
  end;
  if FileExists(StateFile) then begin
    if GetIniString('Release', 'ProductAppId', '', StateFile) <> '{#ProductAppId}' then
      RaiseException(PolicyText('目标目录属于其他产品或包含未知安装状态。',
        'The target directory belongs to another product or contains unknown installation state.'));
    Release := GetIniString('Release', 'Active', '', StateFile);
    if not SafeRelativeName(Release) or (Pos('\', Release) <> 0) or (Pos('/', Release) <> 0) then
      RaiseException(PolicyText('旧安装状态无效。', 'The existing release state is invalid.'));
    StateVersion := GetIniString('Release', 'Version', '', StateFile);
    if not StrToVersion(StateVersion, Previous) then
      RaiseException(PolicyText('旧安装版本号无效或缺失。', 'The existing release version is missing or invalid.'));
    if ComparePackedVersion(Target, Previous) < 0 then
      RaiseException(PolicyText('拒绝降级安装。请使用同版本或更新版本，旧工程和设置保持不变。',
        'Downgrade refused. Use the same or a newer version; existing projects and settings are unchanged.'));
    if not HasRegisteredInstall then
      RaiseException(PolicyText('目标目录有本产品载荷，但缺少安装注册信息，无法确认原快捷方式配置。已停止，未另建副本或删除目录。',
        'Product payload exists without its installation registration. The original shortcuts cannot be verified. Setup stopped without creating another copy or deleting the directory.'));
    // A different side-by-side payload is an update even when the public
    // version remains the same (for example a newer signed test build).
    if (ComparePackedVersion(Target, Previous) > 0) or
      ((ComparePackedVersion(Target, Previous) = 0) and (Release <> '{#ReleaseId}')) then
      InstallationMode := 'Upgrade';
  end;
end;

procedure LockMaintenanceChoices;
begin
  if not HasRegisteredInstall then Exit;
  if CompareText(CanonicalInstallDirectory(WizardDirValue), RegisteredDirectory) <> 0 then
    Log('INSTALL-POLICY: ignoring a requested alternate directory; maintaining the registered installation.');
  WizardForm.DirEdit.Text := RegisteredDirectory;
  WizardForm.DirEdit.ReadOnly := True;
  WizardForm.DirBrowseButton.Enabled := False;
  WizardForm.GroupEdit.Text := RegisteredGroup;
  WizardForm.GroupEdit.ReadOnly := True;
  WizardForm.GroupBrowseButton.Enabled := False;
  WizardForm.NoIconsCheck.Checked := RegisteredNoIcons;
  WizardForm.NoIconsCheck.Enabled := False;
  if RegisteredDesktopIcon then WizardSelectTasks('desktopicon') else WizardSelectTasks('!desktopicon');
  WizardForm.TasksList.Enabled := False;
end;

function InstallationModeCaption: String;
begin
  if InstallationMode = 'Repair' then Result := PolicyText('修复', 'Repair')
  else if InstallationMode = 'Upgrade' then Result := PolicyText('更新', 'Update')
  else if InstallationMode = 'Recovery' then Result := PolicyText('恢复并维护', 'Recover and Maintain')
  else Result := PolicyText('安装', 'Install');
end;

function MaintenanceDescription: String;
begin
  Result := InstallationModeCaption + ' {#AppName}';
  if HasRegisteredInstall then Result := Result + #13#10 + RegisteredVersion + ' → {#AppVersion}' + #13#10 +
    RegisteredDirectory + #13#10 + PolicyText('保留原安装目录、快捷方式、工程和设置；不会另建第二份安装。',
      'Keep the existing directory, shortcuts, projects and settings. No second installation will be created.');
end;

function InitializeSetup: Boolean;
begin
  Result := True;
  try
    ReadInstallationRegistration;
    InstallationMode := 'Install';
    if HasRegisteredInstall then begin
      // An interrupted activation may temporarily disagree with DisplayVersion.
      // Replay under the file lock before making final version decisions.
      if FileExists(AddBackslash(RegisteredDirectory) + '.install-recovery\pending.ini') then
        InstallationMode := 'Recovery'
      else SelectInstallationMode(RegisteredDirectory);
    end;
  except
    Log('INSTALL-POLICY: ' + GetExceptionMessage);
    if not WizardSilent then MsgBox(GetExceptionMessage, mbError, MB_OK);
    Result := False;
  end;
end;

procedure InitializeWizard;
begin
  LockMaintenanceChoices;
  if HasRegisteredInstall then begin
    WizardForm.Caption := InstallationModeCaption + ' {#AppName}';
    WizardForm.WelcomeLabel1.Caption := WizardForm.Caption;
    WizardForm.WelcomeLabel2.Caption := MaintenanceDescription;
  end;
end;

function ShouldSkipPage(PageID: Integer): Boolean;
begin
  Result := HasRegisteredInstall and ((PageID = wpSelectDir) or
    (PageID = wpSelectProgramGroup) or (PageID = wpSelectTasks));
end;

procedure EnforceInstallationIdentity;
var ChosenDirectory: String;
begin
  ChosenDirectory := CanonicalInstallDirectory(ExpandConstant('{app}'));
  ReadInstallationRegistration;
  if HasRegisteredInstall and (CompareText(ChosenDirectory, RegisteredDirectory) <> 0) then
    RaiseException(PolicyText('已有安装的目录已变化；请重新运行安装器，不会在另一目录创建副本。',
      'The registered installation directory changed. Restart Setup; no copy will be created elsewhere.'));
  // Also covers silent /DIR, /GROUP, /NOICONS and /TASKS overrides.
  LockMaintenanceChoices;
end;

procedure ValidateInstallationPolicy;
begin
  EnforceInstallationIdentity;
  SelectInstallationMode(ExpandConstant('{app}'));
  Log('INSTALL-POLICY: Mode=' + InstallationMode + '; Directory=' + ExpandConstant('{app}') +
    '; Version={#AppVersion}; Group=' + WizardGroupValue + '; NoIcons=' + IntToStr(Ord(WizardNoIcons)) +
    '; Desktop=' + IntToStr(Ord(WizardIsTaskSelected('desktopicon'))));
end;

function UpdateReadyMemo(Space, NewLine, MemoUserInfoInfo, MemoDirInfo, MemoTypeInfo,
  MemoComponentsInfo, MemoGroupInfo, MemoTasksInfo: String): String;
begin
  Result := MaintenanceDescription + NewLine + NewLine + MemoDirInfo + NewLine + MemoGroupInfo + NewLine + MemoTasksInfo;
end;

procedure UpdateMaintenancePage(PageID: Integer);
begin
  if HasRegisteredInstall and (PageID = wpReady) then begin
    WizardForm.PageNameLabel.Caption := InstallationModeCaption + ' {#AppName}';
    WizardForm.NextButton.Caption := InstallationModeCaption;
  end;
end;
