#define MyAppName "NovaCraft 1.14"
#define MyAppVersion "1.14.1"
#define MyAppPublisher "NovaCraft"
#define MyAppExeName "NovaCraft.exe"

[Setup]
AppId={{8C09B4C1-1C2D-4F4B-A5D7-1140A14C0001}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={localappdata}\Programs\NovaCraft
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=installer-output
OutputBaseFilename=NovaCraft-1.14-Setup
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#MyAppExeName}
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
SetupLogging=yes
UninstallLogging=yes
RestartApplications=no
CloseApplications=yes
CloseApplicationsFilter=NovaCraft.exe
UsePreviousAppDir=yes
DirExistsWarning=no
Uninstallable=yes

[Tasks]
Name: "desktopicon"; Description: "Создать ярлык на рабочем столе"; GroupDescription: "Дополнительные ярлыки:"; Flags: unchecked

[Files]
Source: "..\publish\NovaCraft.exe"; DestDir: "{app}"; DestName: "{#MyAppExeName}"; Flags: ignoreversion

[Dirs]
Name: "{app}"

[Icons]
Name: "{autoprograms}\NovaCraft 1.14"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\NovaCraft 1.14"; Filename: "{app}\{#MyAppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[UninstallDelete]
Type: filesandordirs; Name: "{app}\logs"

[Code]
function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsWin64 then
  begin
    MsgBox('NovaCraft 1.14 требует 64-разрядную Windows.', mbCriticalError, MB_OK);
    Result := False;
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
var
  InstalledExe: String;
begin
  if CurStep = ssPostInstall then
  begin
    InstalledExe := ExpandConstant('{app}\{#MyAppExeName}');
    if not FileExists(InstalledExe) then
      RaiseException('Установка не завершена: NovaCraft.exe не найден в папке назначения. Проверьте свободное место и права доступа.');
  end;
end;
