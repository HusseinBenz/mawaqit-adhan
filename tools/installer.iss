#ifndef AppVersion
  #define AppVersion "1.0.0-beta.3"
#endif
#ifndef FileVersion
  #define FileVersion "1.0.0.3"
#endif
#ifndef PayloadDir
  #error PayloadDir must point to the staged application files
#endif
#ifndef OutputDir
  #define OutputDir "..\dist"
#endif
#ifdef InstallerTest
  #define ProductId "MawaqitAdhan-InstallerTest"
  #define RunName "MawaqitAdhan-InstallerTest"
  #define LegacyKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\MawaqitAdhan-InstallerTest-Legacy"
#else
  #define ProductId "MawaqitAdhan"
  #define RunName "MawaqitAdhan"
  #define LegacyKey "Software\Microsoft\Windows\CurrentVersion\Uninstall\MawaqitAdhan"
#endif

[Setup]
AppId={#ProductId}
AppName=Mawaqit Adhan
AppVersion={#AppVersion}
AppVerName=Mawaqit Adhan {#AppVersion}
AppPublisher=Hussein Benz
AppPublisherURL=https://github.com/HusseinBenz/mawaqit-adhan
DefaultDirName={code:DefaultDirectory}
DefaultGroupName=Mawaqit Adhan
PrivilegesRequired=lowest
UsePreviousAppDir=yes
DisableDirPage=no
DisableWelcomePage=no
AllowNoIcons=yes
WizardStyle=modern
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\MawaqitAdhan.exe
OutputDir={#OutputDir}
OutputBaseFilename=MawaqitAdhan-{#AppVersion}-Setup
VersionInfoVersion={#FileVersion}
Compression=lzma2
SolidCompression=yes
CloseApplications=yes
RestartApplications=no
CloseApplicationsFilter=*.exe
MinVersion=6.1sp1
SetupMutex=MawaqitAdhan.InnoSetup

[Files]
Source: "{#PayloadDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs

[Icons]
Name: "{group}\Mawaqit Adhan"; Filename: "{app}\MawaqitAdhan.exe"
Name: "{group}\Uninstall Mawaqit Adhan"; Filename: "{uninstallexe}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#RunName}"; ValueData: """{app}\MawaqitAdhan.exe"" --tray"; Check: HadStartup

[Run]
Filename: "{app}\MawaqitAdhan.exe"; Parameters: "--tray"; Description: "Launch Mawaqit Adhan"; Flags: nowait postinstall skipifsilent

[Code]
const
  RunKey = 'Software\Microsoft\Windows\CurrentVersion\Run';
var
  StartupEnabled: Boolean;
  LegacyDirectory: String;
  RunningPaths: TStringList;

function GetCurrentProcessId: LongWord;
  external 'GetCurrentProcessId@kernel32.dll stdcall';
function ProcessIdToSessionId(ProcessId: LongWord; var SessionId: LongWord): Boolean;
  external 'ProcessIdToSessionId@kernel32.dll stdcall';

function AppExists(Path: String): Boolean;
var Version: String;
begin
  Result := (CompareText(ExtractFileName(Path), 'MawaqitAdhan.exe') = 0) and
    FileExists(Path) and GetVersionNumbersString(Path, Version);
end;

function CommandPath(Command: String): String;
var P: Integer;
begin
  Result := '';
  Command := Trim(Command);
  if Command = '' then Exit;
  if Command[1] = '"' then begin
    Delete(Command, 1, 1);
    P := Pos('"', Command);
    if P > 0 then Result := Copy(Command, 1, P - 1);
  end else begin
    P := Pos('.exe', Lowercase(Command));
    if P > 0 then Result := Copy(Command, 1, P + 3);
  end;
end;

function SameDirectory(A, B: String): Boolean;
begin
  Result := CompareText(RemoveBackslashUnlessRoot(ExpandFileName(A)),
    RemoveBackslashUnlessRoot(ExpandFileName(B))) = 0;
end;

function HadStartup: Boolean;
begin
  Result := StartupEnabled;
end;

procedure FindRunningApps;
#ifndef InstallerTest
var Locator, Service, Processes, Process: Variant; I: Integer; Session: LongWord; Path: String;
#endif
begin
  RunningPaths := TStringList.Create;
#ifndef InstallerTest
  try
    if not ProcessIdToSessionId(GetCurrentProcessId, Session) then Exit;
    Locator := CreateOleObject('WbemScripting.SWbemLocator');
    Service := Locator.ConnectServer('', 'root\cimv2');
    Processes := Service.ExecQuery('SELECT ExecutablePath, SessionId FROM Win32_Process WHERE Name="MawaqitAdhan.exe"');
    for I := 0 to Processes.Count - 1 do begin
      Process := Processes.ItemIndex(I);
      if Process.SessionId = Session then begin
        if not VarIsNull(Process.ExecutablePath) then begin
          Path := Process.ExecutablePath;
          if AppExists(Path) then RunningPaths.Add(Path);
        end;
      end;
    end;
  except
    Log('Process discovery unavailable; using registered installation/startup paths.');
  end;
#endif
end;

function DefaultDirectory(Param: String): String;
var Command, Path: String;
begin
  Result := ExpandConstant('{localappdata}\Programs\MawaqitAdhan');
  if (LegacyDirectory <> '') and AppExists(AddBackslash(LegacyDirectory) + 'MawaqitAdhan.exe') then begin
    Result := LegacyDirectory; Exit;
  end;
  if RegQueryStringValue(HKCU, RunKey, '{#RunName}', Command) then begin
    Path := CommandPath(Command);
    if AppExists(Path) then begin Result := ExtractFileDir(Path); Exit; end;
  end;
  if RunningPaths.Count > 0 then begin Result := ExtractFileDir(RunningPaths[0]); Exit; end;
#ifndef InstallerTest
  Path := ExpandConstant('{userdesktop}\adhan\dist\MawaqitAdhan\MawaqitAdhan.exe');
  if AppExists(Path) then Result := ExtractFileDir(Path);
#endif
end;

function InitializeSetup: Boolean;
var Command: String;
begin
  Result := IsDotNetInstalled(net48, 0);
  if not Result then begin
    SuppressibleMsgBox('Mawaqit Adhan requires Microsoft .NET Framework 4.8 or later. Install it from microsoft.com, then run Setup again.', mbError, MB_OK, IDOK);
    Exit;
  end;
  StartupEnabled := RegQueryStringValue(HKCU, RunKey, '{#RunName}', Command);
  RegQueryStringValue(HKCU, '{#LegacyKey}', 'InstallLocation', LegacyDirectory);
  FindRunningApps;
end;

procedure RegisterExtraCloseApplicationsResources;
var I: Integer;
begin
  for I := 0 to RunningPaths.Count - 1 do
    RegisterExtraCloseApplicationsResource(False, RunningPaths[I]);
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var ExistingVersion: String; ExistingPacked, NewPacked: Int64;
begin
  Result := '';
  if GetVersionNumbersString(ExpandConstant('{app}\MawaqitAdhan.exe'), ExistingVersion) then
    if StrToVersion(ExistingVersion, ExistingPacked) and StrToVersion('{#FileVersion}', NewPacked) then
      if ComparePackedVersion(ExistingPacked, NewPacked) > 0 then
        Result := 'A newer Mawaqit Adhan version is already installed in this folder.';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then begin
    { Only remove beta 2's registration/files when upgrading its registered folder. }
    if (LegacyDirectory <> '') and SameDirectory(LegacyDirectory, ExpandConstant('{app}')) then begin
      RegDeleteKeyIncludingSubkeys(HKCU, '{#LegacyKey}');
      DeleteFile(ExpandConstant('{app}\Uninstall.exe'));
      DeleteFile(ExpandConstant('{app}\installation.json'));
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
var Command: String;
begin
  if CurUninstallStep = usPostUninstall then
    if RegQueryStringValue(HKCU, RunKey, '{#RunName}', Command) then
      if CompareText(CommandPath(Command), ExpandConstant('{app}\MawaqitAdhan.exe')) = 0 then
        RegDeleteValue(HKCU, RunKey, '{#RunName}');
  { User settings, cached prayer times and imported voices are never removed. }
end;
