#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{6F0B3C57-3A1E-4D58-9C0B-5B2D8E7A1C42}
AppName=MediaRenamer
AppVersion={#AppVersion}
DefaultDirName={autopf}\MediaRenamer
DefaultGroupName=MediaRenamer
OutputDir=..\dist
SetupIconFile=..\src\MediaRenamer.App\Assets\MediaRenamer.ico
OutputBaseFilename=MediaRenamer-Setup
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
UninstallDisplayIcon={app}\MediaRenamer.App.exe

[Files]
Source: "..\dist\MediaRenamer\*"; DestDir: "{app}"; Flags: recursesubdirs ignoreversion

[Icons]
Name: "{group}\MediaRenamer"; Filename: "{app}\MediaRenamer.App.exe"
Name: "{autodesktop}\MediaRenamer"; Filename: "{app}\MediaRenamer.App.exe"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; Flags: unchecked

[Run]
Filename: "{app}\MediaRenamer.App.exe"; Description: "Launch MediaRenamer"; Flags: nowait postinstall skipifsilent
