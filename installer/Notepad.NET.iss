; Per-user setup of Notepad.NET, built by Build-Installer.ps1:
;   ISCC /DAppVersion=1.2.0 /DArch=x64 /DSourceDir=<folder with Notepad.NET.exe> /DOutDir=<folder> Notepad.NET.iss

[Setup]
AppId={{7E0C2F4A-6B1D-4C58-9A37-2D5E8F1B6C90}
AppName=Notepad.NET
AppVersion={#AppVersion}
AppVerName=Notepad.NET {#AppVersion}
AppPublisher=Notepad.NET contributors
AppPublisherURL=https://github.com/nccnm/Notepad.NET
AppSupportURL=https://github.com/nccnm/Notepad.NET/issues
PrivilegesRequired=lowest
DefaultDirName={autopf}\Notepad.NET
DefaultGroupName=Notepad.NET
DisableProgramGroupPage=yes
LicenseFile=..\LICENSE
OutputDir={#OutDir}
OutputBaseFilename=Notepad.NET-{#AppVersion}-setup-{#Arch}
SetupIconFile=..\src\NotepadNet.App\app.ico
UninstallDisplayIcon={app}\Notepad.NET.exe
UninstallDisplayName=Notepad.NET
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; The app keeps unsaved text as notes when Restart Manager closes it, so nothing is lost by closing it.
CloseApplications=yes
RestartApplications=no
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif

[Tasks]
Name: notepadcommand; Description: "Open Notepad.NET with the &notepad command (PowerShell, cmd and Win+R)"; Flags: unchecked

[Files]
Source: "{#SourceDir}\Notepad.NET.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LICENSE"; DestDir: "{app}"; Flags: ignoreversion
Source: "NotepadCommand.ps1"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\Notepad.NET"; Filename: "{app}\Notepad.NET.exe"

; Not HKCU\Software\Notepad.NET: while that key is missing, the first run of the app takes over the settings
; of the earlier name or of the original Notepad.
[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\App Paths\Notepad.NET.exe"; ValueType: string; ValueName: ""; ValueData: "{app}\Notepad.NET.exe"; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\Notepad.NET.exe\shell\open\command"; ValueType: string; ValueName: ""; ValueData: """{app}\Notepad.NET.exe"" ""%1"""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\Applications\Notepad.NET.exe\SupportedTypes"; ValueType: string; ValueName: ".txt"; ValueData: ""; Flags: uninsdeletekey
Root: HKCU; Subkey: "Software\Classes\.txt\OpenWithList\Notepad.NET.exe"; Flags: uninsdeletekey

[Run]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\NotepadCommand.ps1"" -Enable -Exe ""{app}\Notepad.NET.exe"""; Flags: runhidden; Tasks: notepadcommand; StatusMsg: "Setting up the notepad command..."
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\NotepadCommand.ps1"" -Disable"; Flags: runhidden; Tasks: not notepadcommand
Filename: "{app}\Notepad.NET.exe"; Description: "Start Notepad.NET"; Flags: postinstall nowait skipifsilent unchecked

[UninstallRun]
Filename: "{sys}\WindowsPowerShell\v1.0\powershell.exe"; Parameters: "-NoProfile -NonInteractive -ExecutionPolicy Bypass -File ""{app}\NotepadCommand.ps1"" -Disable"; Flags: runhidden; RunOnceId: "DisableNotepadCommand"

; Settings and notes stay: a note is the only copy of its text.
[UninstallDelete]
Type: files; Name: "{app}\notepad-command.log"
