; Jockey Studio Windows installer (Inno Setup).
;
; Build the self-contained single-file app first, then compile this:
;   dotnet publish src-wpf\JockeyStudio.Wpf\JockeyStudio.Wpf.csproj -c Release -r win-x64 ^
;     --self-contained true -p:PublishSingleFile=true ^
;     -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true ^
;     -p:DebugType=None -p:DebugSymbols=false -o publish_out
;   iscc /DAppVersion=0.2.2 installer\jockeystudio.iss
;
; CI (.github/workflows/build.yml) runs both steps on a v* tag and attaches the
; resulting setup to the release; it is also the payload the in-app updater
; downloads and runs (see Engine/Updater.cs).

#define AppName "Jockey Studio"
#define AppExeName "JockeyStudio.exe"
#define AppPublisher "dev-j33zy"

#ifndef AppVersion
  #define AppVersion "0.2.2"
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
