; ==============================================================================
; 知微 · TeachCraft - Inno Setup 安装脚本
; ==============================================================================

#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif

#ifndef MyAppBaseVersion
  #define MyAppBaseVersion "1.0.0.0"
#endif

#ifndef SourceDir
  #define SourceDir "..\dist\TeachCraft-win-x64-sc"
#endif

#ifndef OutputDir
  #define OutputDir "..\artifacts"
#endif

#ifndef OutputBaseFilename
  #define OutputBaseFilename "TeachCraft-" + MyAppVersion + "-Setup"
#endif

#ifndef Arch
  #define Arch "x64"
#endif

[Setup]
AppId={{08156D55-F74E-4B0B-A562-26D73C0E9CCD}
AppName=知微 · TeachCraft
AppVersion={#MyAppVersion}
AppVerName=知微 · TeachCraft {#MyAppVersion}
AppPublisher=Evan0148
AppPublisherURL=https://github.com/Evan0148/TeachCraft
AppSupportURL=https://github.com/Evan0148/TeachCraft/issues
AppUpdatesURL=https://github.com/Evan0148/TeachCraft/releases

DefaultDirName={autopf}\TeachCraft
DefaultGroupName=知微
AllowNoIcons=yes
DisableProgramGroupPage=yes

PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog commandline

#if Arch == "x64"
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#elif Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#endif

SetupIconFile=..\src\TeachCraft.App\Assets\teachcraft.ico
UninstallDisplayIcon={app}\TeachCraft.exe
WizardStyle=modern

Compression=lzma2/max
SolidCompression=yes

OutputDir={#OutputDir}
OutputBaseFilename={#OutputBaseFilename}
VersionInfoVersion={#MyAppBaseVersion}
VersionInfoDescription=知微 · TeachCraft 安装向导
VersionInfoCopyright=Copyright (C) 2026 Evan0148

[Languages]
Name: "chinesesimplified"; MessagesFile: "Languages\ChineseSimplified.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs; Excludes: "*.pdb,GradeStrip.App.exe"

[Icons]
Name: "{autoprograms}\知微 · TeachCraft"; Filename: "{app}\TeachCraft.exe"
Name: "{autodesktop}\知微 · TeachCraft"; Filename: "{app}\TeachCraft.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\TeachCraft.exe"; Description: "{cm:LaunchProgram,知微 · TeachCraft}"; Flags: nowait postinstall skipifsilent
