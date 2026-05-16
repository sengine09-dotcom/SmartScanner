#define MyAppName      "Smart Scanner"
#define MyAppVersion   "1.0.0"
#define MyAppPublisher "Asian Online Group"
#define MyAppURL       "https://www.asianonlinegroup.co.th"
#define MyAppExeName   "SmartScanner.exe"
#define PublishDir     "publish"

[Setup]
AppId={{14FA89C1-DBF0-4E35-ACA2-004CEC1EA0C4}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
DefaultDirName={autopf32}\{#MyAppName}
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
OutputDir=installer_output
OutputBaseFilename=SmartScanner_Setup_v{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
WizardStyle=modern
WizardSmallImageFile=SmartScanner\Icon\logo_smart_scan.png
PrivilegesRequired=admin
DisableProgramGroupPage=no
UninstallDisplayIcon={app}\{#MyAppExeName}

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a &desktop shortcut"; GroupDescription: "Additional icons:"; Flags: checkedonce

[Files]
; Application files (from dotnet publish output)
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}";                          Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}";                Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}";                    Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; \
  Description: "Launch {#MyAppName}"; \
  Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Remove the SQLite database created at runtime (optional — remove these lines to keep user data on uninstall)
; Type: filesandordirs; Name: "{userappdata}\SmartScanner"
