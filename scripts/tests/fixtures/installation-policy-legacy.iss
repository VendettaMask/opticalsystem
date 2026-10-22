; Non-launchable, isolated setup-policy fixture, not a historical product binary.
#ifndef ProductAppId
  #error ProductAppId is required
#endif
#if ProductAppId == "OpticalSystemDesign-8D387EA8-F349-447F-B5D3-B58E27F46D4A"
  #error Never use the production AppId for a fixture
#endif
[Setup]
AppId={#ProductAppId}
AppName={#AppName}
AppVersion=1.0.0
DefaultDirName={#FixtureDirectory}
DefaultGroupName={#ProductAppId}
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
UsePreviousAppDir=yes
UsePreviousGroup=yes
AllowNoIcons=yes
CloseApplications=no
RestartApplications=no
Uninstallable=yes
SetupLogging=yes
Compression=none
[Files]
Source: "{#PublishDir}\OpticalSystemDesign.App.exe"; DestDir: "{app}"; Flags: ignoreversion
[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\OpticalSystemDesign.App.exe"
