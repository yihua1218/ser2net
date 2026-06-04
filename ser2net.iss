#ifndef MyAppName
#define MyAppName "Ser2Net"
#endif
#ifndef MyAppVersion
#define MyAppVersion "4.6.7"
#endif
#ifndef MyAppPublisher
#define MyAppPublisher "Ser2Net"
#endif
#ifndef MyAppURL
#define MyAppURL "https://github.com/cminyard/ser2net"
#endif
#ifndef SourceDir
#define SourceDir "dist\Ser2Net"
#endif

[Setup]
AppId={{20B100EC-E722-47F4-923A-34ECABC50215}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
PrivilegesRequired=admin
OutputDir=dist\installer
OutputBaseFilename=Ser2Net-{#MyAppVersion}-win64
Compression=lzma
SolidCompression=yes
WizardStyle=modern
ChangesEnvironment=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "installservice"; Description: "Install and start Ser2Net Windows Service"; Flags: checkedonce
Name: "autostarttray"; Description: "Start Ser2Net Manager when Windows starts"; Flags: checkedonce

[Dirs]
Name: "{commonappdata}\Ser2Net"; Permissions: users-modify
Name: "{commonappdata}\Ser2Net\etc"; Permissions: users-modify
Name: "{commonappdata}\Ser2Net\etc\ser2net"; Permissions: users-modify
Name: "{commonappdata}\Ser2Net\logs"; Permissions: users-modify

[Files]
Source: "{#SourceDir}\bin\*"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "{#SourceDir}\etc\*"; DestDir: "{commonappdata}\Ser2Net\etc"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\share\*"; DestDir: "{app}\share"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#SourceDir}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#SourceDir}\man\*"; DestDir: "{app}\man"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\Ser2Net Command Prompt"; Filename: "{cmd}"; Parameters: "/K cd /d ""{app}\bin"""; WorkingDir: "{app}\bin"
Name: "{group}\Ser2Net Manager"; Filename: "{app}\bin\Ser2Net.Tray.exe"; WorkingDir: "{app}\bin"
Name: "{group}\Ser2Net Documentation"; Filename: "{app}\docs"
Name: "{group}\{cm:ProgramOnTheWeb,{#MyAppName}}"; Filename: "{#MyAppURL}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"
Name: "{commonstartup}\Ser2Net Manager"; Filename: "{app}\bin\Ser2Net.Tray.exe"; WorkingDir: "{app}\bin"; Tasks: autostarttray

[Run]
Filename: "{app}\bin\Ser2Net.Service.exe"; Parameters: "install"; Tasks: installservice; Flags: runhidden waituntilterminated
Filename: "{app}\bin\Ser2Net.Service.exe"; Parameters: "start"; Tasks: installservice; Flags: runhidden waituntilterminated
Filename: "{app}\bin\Ser2Net.Tray.exe"; Description: "Launch Ser2Net Manager"; Flags: nowait postinstall skipifsilent

[UninstallRun]
Filename: "{app}\bin\Ser2Net.Service.exe"; Parameters: "stop"; Flags: runhidden waituntilterminated skipifdoesntexist
Filename: "{app}\bin\Ser2Net.Service.exe"; Parameters: "uninstall"; Flags: runhidden waituntilterminated skipifdoesntexist

[Code]
const
  EnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';

function ExecHidden(FileName: string; Params: string; var ResultCode: Integer): Boolean;
begin
  Result := Exec(FileName, Params, '', SW_HIDE, ewWaitUntilTerminated, ResultCode);
end;

procedure TryStopService();
var
  ResultCode: Integer;
begin
  ExecHidden(ExpandConstant('{sys}\sc.exe'), 'stop Ser2Net', ResultCode);
end;

function IsServiceStopped(): Boolean;
var
  ResultCode: Integer;
begin
  Result := ExecHidden(
    ExpandConstant('{cmd}'),
    '/C sc query Ser2Net | find "STOPPED" >nul',
    ResultCode) and (ResultCode = 0);
end;

procedure WaitForServiceStopped();
var
  I: Integer;
begin
  for I := 1 to 20 do begin
    if IsServiceStopped() then
      exit;
    Sleep(500);
  end;
end;

procedure KillProcess(ImageName: string);
var
  ResultCode: Integer;
begin
  ExecHidden(ExpandConstant('{sys}\taskkill.exe'), '/IM "' + ImageName + '" /T /F', ResultCode);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
begin
  TryStopService();
  WaitForServiceStopped();

  KillProcess('Ser2Net.Tray.exe');
  KillProcess('Ser2Net.Service.exe');
  KillProcess('ser2net.exe');

  Result := '';
end;

procedure EnvAddPath(Path: string);
var
  Paths: string;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then
    Paths := '';

  if Pos(';' + Uppercase(Path) + ';', ';' + Uppercase(Paths) + ';') > 0 then
    exit;

  if (Length(Paths) > 0) and (Paths[Length(Paths)] <> ';') then
    Paths := Paths + ';';
  Paths := Paths + Path;

  if RegWriteStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then
    Log(Format('Added [%s] to PATH: [%s]', [Path, Paths]))
  else
    Log(Format('Error while adding [%s] to PATH: [%s]', [Path, Paths]));
end;

procedure EnvRemovePath(Path: string);
var
  Paths: string;
  UpperPaths: string;
  UpperPath: string;
  P: Integer;
begin
  if not RegQueryStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then
    exit;

  UpperPaths := ';' + Uppercase(Paths) + ';';
  UpperPath := ';' + Uppercase(Path) + ';';
  P := Pos(UpperPath, UpperPaths);
  if P = 0 then
    exit;

  Delete(Paths, P, Length(Path) + 1);
  while Pos(';;', Paths) > 0 do
    Delete(Paths, Pos(';;', Paths), 1);
  if (Length(Paths) > 0) and (Paths[1] = ';') then
    Delete(Paths, 1, 1);
  if (Length(Paths) > 0) and (Paths[Length(Paths)] = ';') then
    Delete(Paths, Length(Paths), 1);

  if RegWriteStringValue(HKEY_LOCAL_MACHINE, EnvironmentKey, 'Path', Paths) then
    Log(Format('Removed [%s] from PATH: [%s]', [Path, Paths]))
  else
    Log(Format('Error while removing [%s] from PATH: [%s]', [Path, Paths]));
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    EnvAddPath(ExpandConstant('{app}\bin'));
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
    EnvRemovePath(ExpandConstant('{app}\bin'));
end;
