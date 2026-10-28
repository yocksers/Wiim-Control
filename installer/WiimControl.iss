#define MyAppName "Wiim Control"
#define MyAppVersion "1.28"
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
Source: "{#MyPublishDir}\SMBLibrary.dll"; DestDir: "{app}"; Flags: ignoreversion

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
  CloseRunningApp('{#MyAppExeName}');
  Result := True;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WiimControl');
    RegDeleteValue(HKCU, 'Software\Microsoft\Windows\CurrentVersion\Run', 'WiimVolumeControl');
  end;
end;
