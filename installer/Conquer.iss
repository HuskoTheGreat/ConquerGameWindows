; Inno Setup script for the Conquer Windows installer.
; The setup installs only ConquerLauncher.exe. The launcher downloads the game itself from the latest GitHub build on
; first start and fetches every newer build after that, so the setup never needs rebuilding when the game changes.
; build-installer.cmd publishes the launcher to installer\publish and then compiles this script.
; To compile by hand: iscc /DAppVersion=1.2.3 installer\Conquer.iss

#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "publish"
#endif

#define AppName "Conquer"
#define AppExe "ConquerLauncher.exe"
#define AppPublisher "HuskoTheGreat"
#define AppUrl "https://github.com/HuskoTheGreat/ConquerGameWindows"

[Setup]
; Keep AppId fixed forever: Windows uses it to find the existing install on upgrade and uninstall.
AppId={{1CF0A35F-7B6F-4624-B4B3-830BD7AF022B}
AppName={#AppName}
AppVersion={#AppVersion}
AppVerName={#AppName} {#AppVersion}
AppPublisher={#AppPublisher}
AppPublisherURL={#AppUrl}
AppSupportURL={#AppUrl}/issues
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
; Installs for the current user without an admin prompt; the user can pick "all users" on the first page.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0
OutputDir=output
OutputBaseFilename=ConquerSetup-{#AppVersion}
SetupIconFile=conquer.ico
UninstallDisplayIcon={app}\conquer.ico
UninstallDisplayName={#AppName}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "conquer.ico"; DestDir: "{app}"; Flags: ignoreversion

[UninstallDelete]
; The game files the launcher downloaded for this user. Anything else the game keeps in that folder stays.
Type: filesandordirs; Name: "{localappdata}\Conquer\game"
Type: filesandordirs; Name: "{localappdata}\Conquer\game.new"
Type: filesandordirs; Name: "{localappdata}\Conquer\game.old"
Type: files; Name: "{localappdata}\Conquer\download.zip"
Type: dirifempty; Name: "{localappdata}\Conquer"

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\conquer.ico"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; IconFilename: "{app}\conquer.ico"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "Download and play {#AppName}"; Flags: nowait postinstall skipifsilent
