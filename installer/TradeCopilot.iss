; TradeCopilot.iss — Inno Setup 6 script for the self-contained installer.
; Compile via installer\build.ps1 (passes /DVersion=… from version.txt):
;   ISCC.exe /DVersion=0.2.0 installer\TradeCopilot.iss

#ifndef Version
  #define Version "0.0.0-dev"
#endif

#define MyAppName "TradeCopilot"
#define UIExeName "TradeCopilot.UI.exe"
#define ServiceExeName "TradeCopilot.Service.exe"

[Setup]
; Stable per-app GUID: upgrades install over the previous version.
AppId={{5B2A1F44-8C31-4E7A-9D06-2F7C91D8A3E5}
AppName={#MyAppName}
AppVersion={#Version}
AppPublisher={#MyAppName}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
DisableWelcomePage=yes
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE.txt
OutputDir=out
OutputBaseFilename=TradeCopilot-Setup-{#Version}
; {autopf} and sc.exe both require elevation.
PrivilegesRequired=admin
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
Compression=lzma2
SolidCompression=yes
UninstallDisplayName={#MyAppName} {#Version}
UninstallDisplayIcon={app}\ui\{#UIExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Files]
; Self-contained publish output (installer\build.ps1 writes these).
Source: "out\ui\*"; DestDir: "{app}\ui"; Flags: recursesubdirs ignoreversion
Source: "out\service\*"; DestDir: "{app}\service"; Flags: recursesubdirs ignoreversion
; Piper TTS runtime — the service resolves it as tools/piper/piper.exe relative
; to its working directory ({app}\service). The source zip is not needed.
Source: "..\tools\piper\*"; DestDir: "{app}\service\tools\piper"; Excludes: "piper_windows_amd64.zip"; Flags: recursesubdirs ignoreversion
Source: "..\LICENSE.txt"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\ui\{#UIExeName}"
Name: "{userdesktop}\{#MyAppName}"; Filename: "{app}\ui\{#UIExeName}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: unchecked

[Registry]
; Auto-start the overlay UI at logon for the installing user.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\ui\{#UIExeName}"""; Flags: uninsdeletevalue

[Run]
; Register the background service only if it is not already installed (upgrade
; path). sc.exe requires the space after "create" and after "start=".
; Note: Windows services always launch with Working Directory = System32;
; Program.cs re-anchors it to the exe directory (Directory.SetCurrentDirectory),
; so binPath needs no extra appdir argument.
Filename: "{sys}\cmd.exe"; Parameters: "/c sc query {#MyAppName} >nul 2>&1 || sc create {#MyAppName} start= delayed-auto binPath= ""{app}\service\{#ServiceExeName}"" DisplayName= ""{#MyAppName} background service"""; Flags: runhidden waituntilterminated
; A Windows service runs as LocalSystem, whose %LOCALAPPDATA% is the system
; profile — explicitly point the service at the installing user's per-user
; data directory so it finds the models the first-run wizard downloads into
; %LOCALAPPDATA%\TradeCopilot\models. Written to the service's Environment
; key before sc start, so the value is live for the first launch.
Filename: "{sys}\reg.exe"; Parameters: "add ""HKLM\SYSTEM\CurrentControlSet\Services\{#MyAppName}\Environment"" /v TRADECOPILOT_HOME /t REG_SZ /d ""{localappdata}\{#MyAppName}"" /f"; Flags: runhidden waituntilterminated
Filename: "{sys}\cmd.exe"; Parameters: "/c sc start {#MyAppName}"; Flags: runhidden waituntilterminated
; Launch the overlay unless the install is unattended.
Filename: "{app}\ui\{#UIExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallRun]
; Stop the UI first so it does not reconnect to a service that is going away.
Filename: "{sys}\taskkill.exe"; Parameters: "/IM {#UIExeName} /F"; Flags: runhidden waituntilterminated
Filename: "{sys}\cmd.exe"; Parameters: "/c sc stop {#MyAppName}"; Flags: runhidden waituntilterminated
Filename: "{sys}\cmd.exe"; Parameters: "/c sc delete {#MyAppName}"; Flags: runhidden waituntilterminated
