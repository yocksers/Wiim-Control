#define MyAppName "Wiim Control"
#define MyAppVersion "1.31"
#define MyAppPublisher "Wiim Control"
#define MyAppExeName "WiimControl.exe"
#define MyOldAppName "Wiim Volume Control"
#define MyOldExeName "WiimVolumeControl.exe"
#define MyPublishDir "..\bin\Release\net9.0-windows10.0.19041.0\win-x64\publish"

[Setup]
AppId={{B6C1E9C4-6E3A-4B7B-9C4E-3A6E1F2B5A10}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
UsePreviousGroup=no
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
OutputDir=Output
OutputBaseFilename=WiimControlSetup
SetupIconFile=..\logo.ico
Compression=lzma
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[InstallDelete]
Type: files; Name: "{app}\{#MyOldExeName}"
Type: filesandordirs; Name: "{autoprograms}\{#MyOldAppName}"
Type: files; Name: "{autodesktop}\{#MyOldAppName}.lnk"

[Files]
Source: "{#MyPublishDir}\{#MyAppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyPublishDir}\*.dll"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: WizardSilent

[UninstallDelete]
Type: files; Name: "{app}\wiim.cfg"

[Code]
var
  RemoveEverything: Boolean;

procedure CloseRunningApp(const ExeName: String);
var
  ResultCode: Integer;
  UserFilter: String;
begin
  UserFilter := '/FI "USERNAME eq ' + GetUserNameString + '" ';
  if Exec(ExpandConstant('{sys}\taskkill.exe'), UserFilter + '/IM ' + ExeName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0) then
    Sleep(2000);
  Exec(ExpandConstant('{sys}\taskkill.exe'), UserFilter + '/F /IM ' + ExeName, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  CloseRunningApp('{#MyAppExeName}');
  CloseRunningApp('{#MyOldExeName}');
  Result := '';
end;

function InitializeUninstall(): Boolean;
begin
  RemoveEverything := False;
  CloseRunningApp('{#MyAppExeName}');
  Result := True;
end;

function IsFullPath(const Path: String): Boolean;
begin
  Result := ((Length(Path) >= 3) and (Copy(Path, 2, 2) = ':\')) or ((Length(Path) >= 5) and (Copy(Path, 1, 2) = '\\'));
end;

function TempFolder(): String;
begin
  Result := GetEnv('TMP');
  if Result = '' then Result := GetEnv('TEMP');
  if not IsFullPath(Result) or not DirExists(Result) then Result := ''
  else Result := AddBackslash(Result);
end;

function IsUpdateInstaller(const Name: String): Boolean;
var
  Prefix, Version: String;
  I: Integer;
begin
  Result := False;
  Prefix := 'WiimControlSetup-';
  if Length(Name) <= Length(Prefix) + 4 then Exit;
  if CompareText(Copy(Name, 1, Length(Prefix)), Prefix) <> 0 then Exit;
  if CompareText(Copy(Name, Length(Name) - 3, 4), '.exe') <> 0 then Exit;
  Version := Copy(Name, Length(Prefix) + 1, Length(Name) - Length(Prefix) - 4);
  if (Version[1] = '.') or (Version[Length(Version)] = '.') then Exit;
  for I := 1 to Length(Version) do
    if Pos(Version[I], '0123456789.') = 0 then Exit;
  Result := True;
end;

procedure DeleteUpdateInstallers(const Folder: String);
var
  Found: TFindRec;
begin
  if FindFirst(Folder + 'WiimControlSetup-*.exe', Found) then
  try
    repeat
      if (Found.Attributes and FILE_ATTRIBUTE_DIRECTORY = 0) and IsUpdateInstaller(Found.Name) then
        DeleteFile(Folder + Found.Name);
    until not FindNext(Found);
  finally
    FindClose(Found);
  end;
end;

procedure RemoveUnpackedLibraries(const Temp: String);
begin
  if Temp = '' then Exit;
  if DirExists(Temp + '.net\WiimControl') then
    DelTree(Temp + '.net\WiimControl', True, True, True);
  RemoveDir(Temp + '.net');
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssInstall then RemoveUnpackedLibraries(TempFolder());
end;

procedure RemoveLeftovers();
var
  Temp, App: String;
begin
  Temp := TempFolder();
  if Temp <> '' then
  begin
    RemoveUnpackedLibraries(Temp);
    DeleteUpdateInstallers(Temp);
  end;
  App := ExpandConstant('{app}');
  if IsFullPath(App) and DirExists(App) then
  begin
    App := AddBackslash(App);
    DeleteFile(App + 'wiim.cfg.tmp');
    DeleteFile(App + 'wiim-errors.log');
    DeleteFile(App + 'wiim-errors.log.old');
    RemoveDir(App);
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if (CurUninstallStep = usUninstall) and not UninstallSilent then
    RemoveEverything := MsgBox('Do you also want to remove everything else {#MyAppName} left on this computer?' + #13#10#13#10 +
      'This deletes the error log, leftover update installers and the temporary files {#MyAppName} unpacks when it runs. ' +
      'Nothing else is touched.', mbConfirmation, MB_YESNO or MB_DEFBUTTON2) = IDYES;
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WiimControl');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WiimVolumeControl');
    if RemoveEverything then RemoveLeftovers();
  end;
end;
