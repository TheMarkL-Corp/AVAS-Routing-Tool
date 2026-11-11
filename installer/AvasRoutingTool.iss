; AVAS Routing Tool - single-file installer (Inno Setup 6).
; Build via scripts\build-installer.ps1, which passes /DAppVersion=<csproj version> and /DSourceDir=<staging folder>.

#ifndef AppVersion
  #error AppVersion is not defined. Build with scripts\build-installer.ps1 (passes /DAppVersion=...).
#endif
#ifndef SourceDir
  #error SourceDir is not defined. Build with scripts\build-installer.ps1 (passes /DSourceDir=...).
#endif

[Setup]
; Fixed GUID: identifies this app across versions so upgrades replace the previous install. Never change it.
AppId={{77D4D10F-A2EA-4CC1-9D45-AEE086C02E27}
AppName=AVAS Routing Tool
AppVersion={#AppVersion}
AppPublisher=Advantech
DefaultDirName={autopf}\AVAS Routing Tool
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
OutputDir=Output
OutputBaseFilename=AvasRoutingTool-Setup-{#AppVersion}
SetupIconFile=..\src\AvasRoutingTool\Assets\AppIcon.ico
UninstallDisplayIcon={app}\AvasRoutingTool.exe
Compression=lzma2/ultra64
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
; Everything except the settings file (leading backslash anchors the exclude to the source root)
Source: "{#SourceDir}\*"; DestDir: "{app}"; Excludes: "\appsettings.json"; Flags: recursesubdirs createallsubdirs
; Shipped defaults: written only on a fresh install, kept on upgrade and uninstall
Source: "{#SourceDir}\appsettings.json"; DestDir: "{app}"; Flags: onlyifdoesntexist uninsneveruninstall

[Icons]
Name: "{autoprograms}\AVAS Routing Tool"; Filename: "{app}\AvasRoutingTool.exe"
Name: "{autodesktop}\AVAS Routing Tool"; Filename: "{app}\AvasRoutingTool.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AvasRoutingTool.exe"; Description: "{cm:LaunchProgram,AVAS Routing Tool}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Runtime-generated files that the uninstaller does not know about
Type: filesandordirs; Name: "{app}\WebView2_UserData"
Type: filesandordirs; Name: "{app}\app.log"

[Code]
procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usPostUninstall then
  begin
    { Silent uninstall (/SUPPRESSMSGBOXES) returns IDNO, so settings are kept }
    if SuppressibleMsgBox('Also delete your saved settings?', mbConfirmation, MB_YESNO, IDNO) = IDYES then
      DeleteFile(ExpandConstant('{app}\appsettings.json'));
  end;
end;
