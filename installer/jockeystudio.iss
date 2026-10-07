; Jockey Studio Windows installer (Inno Setup).
;
; Build the self-contained single-file app first, then compile this:
;   dotnet publish src-wpf\JockeyStudio.Wpf\JockeyStudio.Wpf.csproj -c Release -r win-x64 ^
;     --self-contained true -p:PublishSingleFile=true ^
;     -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
;     -p:DebugType=None -p:DebugSymbols=false -o publish_out
;   iscc /DAppVersion=0.2.4 installer\jockeystudio.iss
;
; CI (.github/workflows/build.yml) runs both steps on a v* tag and attaches the
; resulting setup to the release; it is also the payload the in-app updater
; downloads and runs (see Engine/Updater.cs).

#define AppName "Jockey Studio"
#define AppExeName "JockeyStudio.exe"
#define AppPublisher "dev-j33zy"

#ifndef AppVersion
  #define AppVersion "0.2.4"
#endif
#ifndef PublishDir
  #define PublishDir "..\publish_out"
#endif
#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

[Setup]
AppId=com.jockeystudio.app
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
VersionInfoVersion={#AppVersion}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
OutputDir={#OutputDir}
OutputBaseFilename=JockeyStudio_{#AppVersion}_x64-setup
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\src-wpf\JockeyStudio.Wpf\Assets\app.ico
UninstallDisplayIcon={app}\{#AppExeName}
; Per-machine install: the app lives in Program Files, so installing and
; updating both need elevation (UAC). The updater relies on this — it runs
; this same setup with /VERYSILENT.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
; Relaunches the installed app after a manual install (the "Run Jockey Studio"
; finish-page checkbox) and after a silent in-app update. Intentionally no
; skipifsilent: the updater launches this setup with /VERYSILENT and relies on
; this entry to bring the (newly installed) app back up.
Filename: "{app}\{#AppExeName}"; Description: "{cm:LaunchProgram,{#AppName}}"; WorkingDir: "{app}"; Flags: nowait postinstall

[Code]
// The 0.1.x (Tauri/Rust) app was a separate per-user product: its own install
// folder, Start Menu and desktop shortcuts, and Add/Remove Programs entry, filed
// under a different identifier. It shares no AppId with this installer, so an
// update cannot supersede it - a machine that ran 0.1.x keeps a second "Jockey
// Studio" in the Start menu and in Programs, and the stale per-user shortcut is
// the one that wins, so clicking it launches the old app even though the update
// itself succeeded.
//
// 0.1.x installed per user, so every account that ran it has its own leftovers
// and only the account running Setup can be reached with {localappdata} and
// HKCU. Walk the machine's profiles instead, so one install clears every account
// however many are signed up on it.

procedure RemoveLegacyApp(const ProfileDir, Sid: String);
var
  Dir, Local, Roaming, Programs, Desktop: String;
begin
  if ProfileDir = '' then
    Exit;

  // ProfileImagePath carries no trailing separator, but tolerate one so the
  // paths below cannot come out malformed.
  Dir := ProfileDir;
  if (Length(Dir) > 0) and (Copy(Dir, Length(Dir), 1) = '\') then
    Delete(Dir, Length(Dir), 1);
  if Dir = '' then
    Exit;

  Local    := Dir + '\AppData\Local';
  Roaming  := Dir + '\AppData\Roaming';
  Programs := Roaming + '\Microsoft\Windows\Start Menu\Programs';
  Desktop  := Dir + '\Desktop';

  // The app itself and the data folders older builds wrote under the old bundle
  // identifier. Never touches com.jockeystudio.app, this app's own settings.
  DelTree(Local + '\Jockey Studio', True, True, True);
  DelTree(Local + '\Programs\Jockey Studio', True, True, True);
  DelTree(Local + '\com.cjaycapillo.jockeystudio', True, True, True);
  DelTree(Roaming + '\com.cjaycapillo.jockeystudio', True, True, True);

  // The old shortcuts. The Start Menu one is what keeps launching 0.1.x after
  // an otherwise successful update.
  DeleteFile(Programs + '\Jockey Studio.lnk');
  DelTree(Programs + '\Jockey Studio', True, True, True);
  DeleteFile(Desktop + '\Jockey Studio.lnk');

  // That account's Programs entry, filed under the old name and identifier.
  // Windows only mounts a signed-in account's hive, so this reaches whoever is
  // in a session now; a logged-off account keeps an inert entry until it next
  // signs in, and the app it points at has just been deleted anyway.
  if Sid <> '' then
    RegDeleteKeyIncludingSubkeys(HKU, Sid + '\Software\Microsoft\Windows\CurrentVersion\Uninstall\Jockey Studio');
end;

procedure RemoveLegacyInstalls;
var
  Sids: TArrayOfString;
  ProfileKey, Image: String;
  I: Integer;
begin
  // ProfileList is the machine's list of profiles, one key per account.
  ProfileKey := 'SOFTWARE\Microsoft\Windows NT\CurrentVersion\ProfileList';
  if not RegGetSubkeyNames(HKLM, ProfileKey, Sids) then
    Exit;

  for I := 0 to GetArrayLength(Sids) - 1 do
  begin
    Image := '';
    if RegQueryStringValue(HKLM, ProfileKey + '\' + Sids[I], 'ProfileImagePath', Image) then
      RemoveLegacyApp(ExpandConstant(Image), Sids[I]);
  end;
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  // Runs after this install's own files, shortcuts and Programs entry are in
  // place, so it only ever removes what the 0.1.x build left behind. Also skips
  // %TEMP%\jockey-studio-update\, which is not a 0.1.x leftover but the staging
  // folder for the very setup the in-app updater is running here.
  if CurStep = ssPostInstall then
    RemoveLegacyInstalls;
end;
