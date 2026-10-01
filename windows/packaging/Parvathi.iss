#ifndef AppVersion
  #define AppVersion "0.2.0"
#endif
#ifndef PublishDir
  #error PublishDir must point to the self-contained win-x64 publish folder.
#endif
#ifndef ArtifactDir
  #error ArtifactDir must point to the release asset folder.
#endif

[Setup]
AppId={{AC15950F-96F8-4590-B247-8D895555E7A0}
AppName=Parvathi
AppVersion={#AppVersion}
AppPublisher=Parvathi contributors
AppPublisherURL=https://github.com/KrishnaVarun02/Parvathi
AppSupportURL=https://github.com/KrishnaVarun02/Parvathi/issues
AppUpdatesURL=https://github.com/KrishnaVarun02/Parvathi/releases
DefaultDirName={localappdata}\Programs\Parvathi
DefaultGroupName=Parvathi
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible and not arm64
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.22000
DisableProgramGroupPage=yes
DisableWelcomePage=no
UsePreviousAppDir=yes
UsePreviousTasks=yes
AppMutex=Local\Parvathi.Windows
CloseApplications=no
RestartApplications=no
UninstallDisplayIcon={app}\Parvathi.exe
SetupIconFile={#PublishDir}\Assets\Parvathi.ico
OutputDir={#ArtifactDir}
OutputBaseFilename=Parvathi-Setup-{#AppVersion}-win-x64
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
InfoBeforeFile=INSTALLATION.txt
Uninstallable=yes
UninstallDisplayName=Parvathi
VersionInfoVersion={#AppVersion}.0
#ifdef SignBuild
SignTool=parvathi
SignedUninstaller=yes
#endif

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Shortcuts:"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{userprograms}\Parvathi"; Filename: "{app}\Parvathi.exe"; WorkingDir: "{app}"
Name: "{userdesktop}\Parvathi"; Filename: "{app}\Parvathi.exe"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\Parvathi.exe"; Description: "Launch Parvathi"; Flags: nowait postinstall skipifsilent

[Registry]
; Remove only the startup entry that the application itself may opt into.
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueName: "Parvathi"; Flags: uninsdeletevalue

; User settings, downloaded models, history and credentials are deliberately outside
; {app}. Upgrades and uninstall do not delete them. Clear history/key inside Settings.
