// Durable journal shared with WindowsInstallationRecovery.cs (format 2).
function JournalPath: String;
begin Result := ExpandConstant('{app}\.install-recovery\pending.ini'); end;

procedure FlushDurableFile(Name: String);
var Handle: THandle;
begin
  Handle := CreateFileW(Name, $40000000, 3, 0, 3, $80000000, 0);
  if Handle = THandle(-1) then RaiseException('无法打开恢复日志以持久保存：' + Name);
  try
    if not FlushFileBuffers(Handle) then RaiseException('恢复日志未能写入磁盘：' + Name);
  finally CloseHandle(Handle); end;
end;

procedure JournalSet(FileName, Section, Name, Value: String);
var Encoded, Digits: String; I, Code: Integer;
begin
  Encoded := 'u:'; Digits := '0123456789ABCDEF';
  for I := 1 to Length(Value) do begin
    Code := Ord(Value[I]);
    Encoded := Encoded + Digits[(Code div 4096) + 1] + Digits[((Code div 256) mod 16) + 1] +
      Digits[((Code div 16) mod 16) + 1] + Digits[(Code mod 16) + 1];
  end;
  if not SetIniString(Section, Name, Encoded, FileName) then RaiseException('无法写入恢复日志。');
end;

function JournalGet(Section, Name, Default, FileName: String): String;
var Encoded: String; I, Code: Integer;
begin
  Encoded := GetIniString(Section, Name, '', FileName);
  if (Copy(Encoded, 1, 2) <> 'u:') or ((Length(Encoded) - 2) mod 4 <> 0) then
    RaiseException('安装恢复日志编码或字段无效：' + Section + '/' + Name);
  Result := ''; I := 3;
  while I <= Length(Encoded) do begin
    Code := StrToIntDef('$' + Copy(Encoded, I, 4), -1);
    if (Code < 0) or (Code > 65535) then RaiseException('安装恢复日志字符无效。');
    Result := Result + Chr(Code); I := I + 4;
  end;
end;

procedure PublishJournal(Temporary: String);
begin
  // The Win32 profile flush call can return zero even when flushing succeeds.
  WritePrivateProfileStringW(0, 0, 0, Temporary);
  FlushDurableFile(Temporary);
  if not MoveFileExW(Temporary, JournalPath, 9) then RaiseException('无法原子保存恢复日志。');
end;

procedure WriteJournalPhase(Phase: String);
var Temporary: String;
begin
  if not FileExists(JournalPath) then RaiseException('安装恢复日志缺失。');
  Temporary := JournalPath + '.tmp';
  if not FileCopy(JournalPath, Temporary, False) then RaiseException('无法更新恢复日志。');
  JournalSet(Temporary, 'Transaction', 'Phase', Phase);
  PublishJournal(Temporary);
end;

function UninstallKey: String;
begin Result := 'Software\Microsoft\Windows\CurrentVersion\Uninstall\{#ProductAppId}_is1'; end;

procedure CheckPayloadTree(Directory: String; Depth: Integer; var Entries: Integer);
var Search: TFindRec; Path: String;
begin
  if Depth > 64 then RaiseException('安装载荷目录层级过深。');
  CheckNoReparsePath(Directory);
  if FindFirst(Directory + '\*', Search) then begin
    try
      repeat
        if (Search.Name <> '.') and (Search.Name <> '..') then begin
          Entries := Entries + 1;
          if Entries > 10000 then RaiseException('安装载荷目录条目过多。');
          Path := Directory + '\' + Search.Name;
          if (Search.Attributes and $400) <> 0 then RaiseException('安装载荷不能包含目录联接或符号链接。');
          if (Search.Attributes and $10) <> 0 then CheckPayloadTree(Path, Depth + 1, Entries);
        end;
      until not FindNext(Search);
    finally FindClose(Search); end;
  end;
end;

procedure CaptureDurableJournal;
var Temporary, Backup, Key, Text, Base, Extension: String; Names: TArrayOfString; I: Integer;
    Number, Written: Cardinal; Handle: THandle;
begin
  if not ForceDirectories(ExtractFileDir(JournalPath)) then RaiseException('无法创建安装恢复目录。');
  CheckNoReparsePath(ExtractFileDir(JournalPath));
  Temporary := JournalPath + '.tmp';
  CheckNoReparsePath(Temporary);
  // Write a UTF-16LE BOM without locale-dependent AnsiString conversion.
  Handle := CreateFileW(Temporary, $40000000, 0, 0, 2, $80000000, 0);
  if Handle = THandle(-1) then RaiseException('无法创建恢复日志。');
  try
    if not WriteFile(Handle, #$FEFF, 2, Written, 0) or (Written <> 2) or not FlushFileBuffers(Handle) then
      RaiseException('无法创建 Unicode 恢复日志。');
  finally CloseHandle(Handle); end;
  JournalSet(Temporary, 'Transaction', 'FormatVersion', '2');
  JournalSet(Temporary, 'Transaction', 'ProductAppId', '{#ProductAppId}');
  JournalSet(Temporary, 'Transaction', 'Root', ExpandConstant('{app}'));
  JournalSet(Temporary, 'Transaction', 'Phase', 'Captured');
  JournalSet(Temporary, 'Transaction', 'StateExists', IntToStr(Ord(PreviousStateExists)));
  if PreviousStateExists then begin
    Backup := ExtractFileDir(JournalPath) + '\previous-state.ini';
    if not FileCopy(StatePath, Backup, False) then RaiseException('无法保存旧版本状态。');
    FlushDurableFile(Backup);
    JournalSet(Temporary, 'Transaction', 'StateSha256', GetSHA256OfFile(Backup));
  end;
  for I := 0 to 2 do begin
    JournalSet(Temporary, 'Shortcut' + IntToStr(I), 'Path', ShortcutPaths[I]);
    JournalSet(Temporary, 'Shortcut' + IntToStr(I), 'Exists', IntToStr(Ord(ShortcutExisted[I])));
    if ShortcutExisted[I] then begin
      Backup := ExtractFileDir(JournalPath) + '\shortcut-' + IntToStr(I) + '.lnk';
      if not FileCopy(ShortcutPaths[I], Backup, False) then RaiseException('无法保存旧快捷方式。');
      FlushDurableFile(Backup);
      JournalSet(Temporary, 'Shortcut' + IntToStr(I), 'Sha256', GetSHA256OfFile(Backup));
    end;
  end;
  JournalSet(Temporary, 'Association', 'Owned', IntToStr(Ord(AssociationOwned)));
  JournalSet(Temporary, 'Association', 'Command', PreviousCommand);
  JournalSet(Temporary, 'Association', 'IconExists', IntToStr(Ord(PreviousIconExists)));
  JournalSet(Temporary, 'Association', 'Icon', PreviousIcon);
  Key := UninstallKey;
  Base := '';
  if RegQueryStringValue(HKCU, Key, 'UninstallString', Text) then begin
    if (Length(Text) > 2) and (Text[1] = '"') and (Text[Length(Text)] = '"') then
      Text := Copy(Text, 2, Length(Text) - 2);
    if CompareText(ExtractFileDir(Text), ExpandConstant('{app}')) <> 0 then
      RaiseException('旧卸载入口不属于本安装目录，安装尚未开始。');
    Base := ChangeFileExt(ExtractFileName(Text), '');
    if not SafeRelativeName(Base) or (CompareText(Copy(Base, 1, 5), 'unins') <> 0) then
      RaiseException('旧卸载入口名称无效。');
  end;
  JournalSet(Temporary, 'Uninstaller', 'BaseName', Base);
  for I := 0 to 2 do begin
    if I = 0 then Extension := '.exe' else if I = 1 then Extension := '.dat' else Extension := '.msg';
    JournalSet(Temporary, 'Uninstaller' + Extension, 'Exists', IntToStr(Ord((Base <> '') and FileExists(ExpandConstant('{app}\') + Base + Extension))));
    if (Base <> '') and FileExists(ExpandConstant('{app}\') + Base + Extension) then begin
      Backup := ExtractFileDir(JournalPath) + '\uninstaller' + Extension;
      CheckNoReparsePath(ExpandConstant('{app}\') + Base + Extension);
      if not FileCopy(ExpandConstant('{app}\') + Base + Extension, Backup, False) then RaiseException('无法备份旧卸载程序。');
      FlushDurableFile(Backup);
      JournalSet(Temporary, 'Uninstaller' + Extension, 'Sha256', GetSHA256OfFile(Backup));
    end;
  end;
  JournalSet(Temporary, 'Registry', 'Exists', IntToStr(Ord(RegKeyExists(HKCU, Key))));
  SetArrayLength(Names, 0);
  if RegKeyExists(HKCU, Key) and not RegGetValueNames(HKCU, Key, Names) then
    RaiseException('无法保存卸载注册信息。');
  if GetArrayLength(Names) > 256 then RaiseException('卸载注册信息过大。');
  JournalSet(Temporary, 'Registry', 'Count', IntToStr(GetArrayLength(Names)));
  for I := 0 to GetArrayLength(Names) - 1 do begin
    JournalSet(Temporary, 'Registry' + IntToStr(I), 'Name', Names[I]);
    if RegQueryDWordValue(HKCU, Key, Names[I], Number) then begin
      JournalSet(Temporary, 'Registry' + IntToStr(I), 'Kind', 'DWord');
      JournalSet(Temporary, 'Registry' + IntToStr(I), 'Data', IntToStr(Number));
    end else if RegQueryStringValue(HKCU, Key, Names[I], Text) then begin
      JournalSet(Temporary, 'Registry' + IntToStr(I), 'Kind', 'String');
      JournalSet(Temporary, 'Registry' + IntToStr(I), 'Data', Text);
    end else RaiseException('不支持的卸载注册数据类型，安装尚未开始。');
  end;
  PublishJournal(Temporary);
end;

procedure CheckBackup(Backup, Expected: String);
begin
  CheckNoReparsePath(Backup);
  if (Length(Expected) <> 64) or not FileExists(Backup) or (GetSHA256OfFile(Backup) <> Expected) then
    RaiseException('安装恢复备份校验失败；保留日志，请勿删除备份。');
end;

procedure RestoreDurableFile(Backup, Target: String);
var Temporary: String;
begin
  CheckNoReparsePath(Target);
  Temporary := Target + '.recovery.tmp';
  if not ForceDirectories(ExtractFileDir(Target)) or not FileCopy(Backup, Temporary, False) then
    RaiseException('无法恢复旧入口：' + Target);
  FlushDurableFile(Temporary);
  if not MoveFileExW(Temporary, Target, 9) then RaiseException('无法原子恢复旧入口：' + Target);
end;

procedure RecoverInterruptedTransaction;
var J, Directory, Path, Section, Key, Kind, Name, Text, Base, Extension: String; I, Count, K: Integer;
    Names: TArrayOfString; Found: Boolean; Number: Cardinal;
begin
  J := JournalPath;
  if not FileExists(J) then Exit;
  CheckNoReparsePath(J);
  if (JournalGet('Transaction', 'FormatVersion', '', J) <> '2') or
     (JournalGet('Transaction', 'ProductAppId', '', J) <> '{#ProductAppId}') or
     (CompareText(JournalGet('Transaction', 'Root', '', J), ExpandConstant('{app}')) <> 0) then
    RaiseException('安装恢复日志的产品或目录身份不匹配。');
  if JournalGet('Transaction', 'Phase', '', J) = 'Committed' then begin
    if not DeleteFile(J) then RaiseException('无法完成旧安装日志清理。');
    Exit;
  end;
  Directory := ExtractFileDir(J);
  if JournalGet('Transaction', 'StateExists', '', J) = '1' then
    CheckBackup(Directory + '\previous-state.ini', JournalGet('Transaction', 'StateSha256', '', J));
  for I := 0 to 2 do begin
    Section := 'Shortcut' + IntToStr(I);
    Path := JournalGet(Section, 'Path', '', J);
    if Path <> '' then begin
      if Pos('..', Path) <> 0 then RaiseException('恢复快捷方式路径无效。');
      if (CompareText(Copy(Path, 1, Length(ExpandConstant('{userprograms}\'))), ExpandConstant('{userprograms}\')) <> 0) and
         (CompareText(Copy(Path, 1, Length(ExpandConstant('{userdesktop}\'))), ExpandConstant('{userdesktop}\')) <> 0) then
        RaiseException('恢复快捷方式不属于当前用户桌面或开始菜单。');
      if not SafeRelativeName(ExtractFileName(Path)) then RaiseException('恢复快捷方式名称无效。');
      CheckNoReparsePath(Path);
    end;
    if JournalGet(Section, 'Exists', '', J) = '1' then
      CheckBackup(Directory + '\shortcut-' + IntToStr(I) + '.lnk', JournalGet(Section, 'Sha256', '', J));
  end;
  Count := StrToIntDef(JournalGet('Registry', 'Count', '', J), -1);
  if (Count < 0) or (Count > 256) then RaiseException('恢复注册信息数量无效。');
  for I := 0 to Count - 1 do begin
    Kind := JournalGet('Registry' + IntToStr(I), 'Kind', '', J);
    if (Kind <> 'String') and (Kind <> 'DWord') then RaiseException('恢复注册信息类型无效。');
    if Kind = 'DWord' then
      if (StrToInt64Def(JournalGet('Registry' + IntToStr(I), 'Data', '', J), -1) < 0) or
         (StrToInt64Def(JournalGet('Registry' + IntToStr(I), 'Data', '', J), -1) > $FFFFFFFF) then
        RaiseException('恢复注册整数无效。');
  end;
  Base := JournalGet('Uninstaller', 'BaseName', '', J);
  if (Base <> '') and (not SafeRelativeName(Base) or (Pos('\', Base) <> 0) or (Pos('/', Base) <> 0) or
      (CompareText(Copy(Base, 1, 5), 'unins') <> 0)) then RaiseException('恢复卸载程序名称无效。');
  for I := 0 to 2 do begin
    if I = 0 then Extension := '.exe' else if I = 1 then Extension := '.dat' else Extension := '.msg';
    if JournalGet('Uninstaller' + Extension, 'Exists', '', J) = '1' then
      CheckBackup(Directory + '\uninstaller' + Extension, JournalGet('Uninstaller' + Extension, 'Sha256', '', J));
  end;
  Log('Recovering interrupted installation from durable journal.');
  if JournalGet('Transaction', 'StateExists', '', J) = '1' then
    RestoreDurableFile(Directory + '\previous-state.ini', StatePath)
  else if FileExists(StatePath) and not DeleteFile(StatePath) then RaiseException('无法恢复无版本状态。');
  for I := 0 to 2 do begin
    Section := 'Shortcut' + IntToStr(I); Path := JournalGet(Section, 'Path', '', J);
    if JournalGet(Section, 'Exists', '', J) = '1' then
      RestoreDurableFile(Directory + '\shortcut-' + IntToStr(I) + '.lnk', Path)
    else if (Path <> '') and FileExists(Path) and not DeleteFile(Path) then RaiseException('无法清理中断安装入口。');
  end;
  Key := UninstallKey;
  if JournalGet('Registry', 'Exists', '', J) = '1' then begin
    SetArrayLength(Names, 0); RegGetValueNames(HKCU, Key, Names);
    for K := 0 to GetArrayLength(Names) - 1 do begin
      Found := False;
      for I := 0 to Count - 1 do
        if CompareText(Names[K], JournalGet('Registry' + IntToStr(I), 'Name', '', J)) = 0 then Found := True;
      if not Found and not RegDeleteValue(HKCU, Key, Names[K]) then RaiseException('无法恢复卸载注册字段。');
    end;
    for I := 0 to Count - 1 do begin
      Section := 'Registry' + IntToStr(I); Name := JournalGet(Section, 'Name', '', J);
      Text := JournalGet(Section, 'Data', '', J);
      if JournalGet(Section, 'Kind', '', J) = 'DWord' then begin
        Number := StrToInt64Def(Text, -1);
        if not RegWriteDWordValue(HKCU, Key, Name, Number) then RaiseException('无法恢复卸载注册字段。');
      end else if not RegWriteStringValue(HKCU, Key, Name, Text) then RaiseException('无法恢复卸载注册字段。');
    end;
  end else if RegKeyExists(HKCU, Key) and not RegDeleteKeyIncludingSubkeys(HKCU, Key) then
    RaiseException('无法清理未完成安装的卸载注册信息。');
  if JournalGet('Association', 'Owned', '', J) = '1' then begin
    if not RegWriteStringValue(HKCU, 'Software\Classes\{#AssociationId}\shell\open\command', '',
        JournalGet('Association', 'Command', '', J)) then RaiseException('无法恢复工程打开命令。');
    Key := 'Software\Classes\{#AssociationId}\DefaultIcon';
    if JournalGet('Association', 'IconExists', '', J) = '1' then begin
      if not RegWriteStringValue(HKCU, Key, '', JournalGet('Association', 'Icon', '', J)) then RaiseException('无法恢复工程图标。');
    end else if RegValueExists(HKCU, Key, '') and not RegDeleteValue(HKCU, Key, '') then RaiseException('无法恢复工程图标。');
  end;
  if Base <> '' then
    for I := 0 to 2 do begin
      if I = 0 then Extension := '.exe' else if I = 1 then Extension := '.dat' else Extension := '.msg';
      Path := ExpandConstant('{app}\') + Base + Extension;
      if JournalGet('Uninstaller' + Extension, 'Exists', '', J) = '1' then
        RestoreDurableFile(Directory + '\uninstaller' + Extension, Path)
      else if FileExists(Path) and not DeleteFile(Path) then RaiseException('无法恢复旧卸载文件状态。');
    end;
  if not DeleteFile(J) then RaiseException('恢复已完成但日志未清理；下次将再次校验恢复。');
end;

#ifdef ValidationHooks
procedure PauseForInterruptionTest(Parameter: String);
var Marker: String; I: Integer;
begin
  Marker := ExpandConstant('{param:' + Parameter + '|}');
  if Marker = '' then Exit;
  if not SaveStringToFile(Marker, IntToStr(GetCurrentProcessId), False) then RaiseException('无法写入强杀测试标记。');
  for I := 0 to 600 do Sleep(100);
  RaiseException('强杀测试超时。');
end;
#endif
