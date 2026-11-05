#ifndef MyAppVersion
  #error MyAppVersion fehlt. scripts/Build-Installer.ps1 verwenden.
#endif

[Setup]
AppId={{6798D3CD-9220-4F7F-A95A-17BBCE971E32}
AppName=VIBN Tools
AppVersion={#MyAppVersion}
DefaultDirName={autopf}\VIBN Tools
DefaultGroupName=VIBN Tools
OutputDir=..\artifacts\installer
OutputBaseFilename=VIBN_Tools_Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
WizardStyle=modern
UninstallDisplayIcon={app}\VIBN_Tools.exe

[Files]
Source: "..\artifacts\publish\VIBN_Tools-win-x64\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\VIBN Tools"; Filename: "{app}\VIBN_Tools.exe"
Name: "{autodesktop}\VIBN Tools"; Filename: "{app}\VIBN_Tools.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Desktopverknüpfung erstellen"; GroupDescription: "Zusätzliche Symbole:"

[Run]
Filename: "{app}\VIBN_Tools.exe"; Description: "VIBN Tools starten"; Flags: nowait postinstall skipifsilent

[Code]
function InitializeSetup(): Boolean;
var
  Release: Cardinal;
begin
  Result := RegQueryDWordValue(HKLM32,
    'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release);
  if Result then
    Result := Release >= 528040;
  if not Result then
    MsgBox('Die TIA-Bridge benötigt .NET Framework 4.8 oder neuer. Bitte die Microsoft-Laufzeit installieren und das Setup erneut starten.', mbError, MB_OK);
end;
