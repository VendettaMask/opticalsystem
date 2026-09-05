; Compile via scripts/build-installer.ps1. No third-party executable is fetched by Setup.
#if Ver < EncodeVer(6, 3, 0)
  #error Inno Setup 6.3 or later is required
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef AppVersion
  #error AppVersion is required
#endif
#ifndef TargetRuntime
  #error TargetRuntime is required
#endif
#ifndef ProductAppId
  #define ProductAppId "OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A"
#endif
#define AppName "Optical System Design"
#define AppExe "OptilandWorkbench.App.exe"

[Setup]
AppId={#ProductAppId}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=S.T.A.R. Labs
DefaultDirName={localappdata}\Programs\S.T.A.R. Labs\Optical System Design
DefaultGroupName=S.T.A.R. Labs\Optical System Design
PrivilegesRequired=lowest
WizardStyle=modern
DisableWelcomePage=no
DisableDirPage=no
DisableProgramGroupPage=no
AllowNoIcons=yes
UsePreviousAppDir=yes
UsePreviousTasks=yes
MinVersion=10.0.19045
#if TargetRuntime == "win-arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#elif TargetRuntime == "win-x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#else
  #error Unsupported TargetRuntime
#endif
SetupIconFile={#PublishDir}\Assets\Brand\AppIcon.ico
UninstallDisplayIcon={app}\{#AppExe}
UninstallDisplayName={#AppName}
VersionInfoDescription=Optical System Design Setup
Compression=none
SolidCompression=no
CloseApplications=no
RestartApplications=no
Uninstallable=yes
SetupLogging=yes
OutputBaseFilename=OpticalSystemDesign-{#AppVersion}-{#TargetRuntime}-Setup

[Languages]
Name: "chinesesimp"; MessagesFile: "inno\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "inno\LICENSE.txt"; DestDir: "{app}\ThirdParty\InnoSetup"; Flags: ignoreversion
Source: "inno\SOURCE.md"; DestDir: "{app}\ThirdParty\InnoSetup"; Flags: ignoreversion

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"
Name: "{group}\{cm:UninstallProgram,{#AppName}}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; WorkingDir: "{app}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchProgram,{#AppName}}"; Flags: nowait postinstall skipifsilent unchecked

; Inno's uninstall log owns only installed files and shortcuts. Do not add
; recursive InstallDelete/UninstallDelete entries or delete user settings/projects.
