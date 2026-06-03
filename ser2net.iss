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

[Files]
Source: "{#SourceDir}\bin\*"; DestDir: "{app}\bin"; Flags: ignoreversion
Source: "{#SourceDir}\etc\*"; DestDir: "{app}\etc"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#SourceDir}\share\*"; DestDir: "{app}\share"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#SourceDir}\docs\*"; DestDir: "{app}\docs"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist
Source: "{#SourceDir}\man\*"; DestDir: "{app}\man"; Flags: ignoreversion recursesubdirs createallsubdirs skipifsourcedoesntexist

[Icons]
Name: "{group}\Ser2Net Command Prompt"; Filename: "{cmd}"; Parameters: "/K cd /d ""{app}\bin"""; WorkingDir: "{app}\bin"
Name: "{group}\Ser2Net Documentation"; Filename: "{app}\docs"
Name: "{group}\{cm:ProgramOnTheWeb,{#MyAppName}}"; Filename: "{#MyAppURL}"
Name: "{group}\{cm:UninstallProgram,{#MyAppName}}"; Filename: "{uninstallexe}"

[Code]
const
  EnvironmentKey = 'SYSTEM\CurrentControlSet\Control\Session Manager\Environment';

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
