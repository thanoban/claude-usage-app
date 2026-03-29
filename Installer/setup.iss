; Claude Usage - Inno Setup Script
; Produces: claude-usage-setup.exe
; No admin rights required

#define AppName "Claude Usage"
#define AppVersion "1.0.0"
#define AppPublisher "ClaudeUsage"
#define AppExeName "ClaudeUsage.exe"
#define AppId "{{A7B3C2D1-E4F5-4321-8765-ABCDEF012345}"

[Setup]
AppId={#AppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL=https://github.com/
DefaultDirName={localappdata}\ClaudeUsage
DefaultGroupName={#AppName}
OutputDir=..\dist
OutputBaseFilename=claude-usage-setup
Compression=lzma
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\{#AppExeName}
ArchitecturesInstallIn64BitMode=x64
MinVersion=10.0

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start {#AppName} automatically when Windows starts"; \
  GroupDescription: "Additional options:"; Flags: checked

[Files]
; The single self-contained exe built via dotnet publish
Source: "..\bin\Release\net8.0-windows\win-x64\publish\ClaudeUsage.exe"; \
  DestDir: "{app}"; Flags: ignoreversion

[Icons]
; Start Menu
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall {#AppName}"; Filename: "{uninstallexe}"

; Desktop shortcut (optional – comment out if unwanted)
; Name: "{userdesktop}\{#AppName}"; Filename: "{app}\{#AppExeName}"

[Registry]
; Auto-start on login
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; \
  ValueType: string; ValueName: "{#AppName}"; \
  ValueData: """{app}\{#AppExeName}"""; \
  Flags: uninsdeletevalue; Tasks: startup

[Run]
; Launch after install
Filename: "{app}\{#AppExeName}"; \
  Description: "Launch {#AppName} now"; \
  Flags: nowait postinstall skipifsilent

[UninstallRun]
; Kill the process if running before uninstall
Filename: "taskkill"; Parameters: "/F /IM {#AppExeName}"; \
  Flags: runhidden skipifdoesntexist

[UninstallDelete]
; Remove settings (optional — comment out to preserve user data)
; Type: filesandordirs; Name: "{localappdata}\ClaudeUsage"
