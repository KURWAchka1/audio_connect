#ifndef AppVersion
  #define AppVersion "1.1.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\dist\windows"
#endif
#ifndef OutputDirPath
  #define OutputDirPath "..\dist\releases"
#endif

[Setup]
AppId={{93DC9D2B-B868-4D7F-86B5-94C48D73C036}
AppName=Audio Connect
AppVersion={#AppVersion}
AppPublisher=KURWAchka1
AppPublisherURL=https://github.com/KURWAchka1/audio_connect
AppSupportURL=https://github.com/KURWAchka1/audio_connect/issues
DefaultDirName={localappdata}\Programs\Audio Connect
DefaultGroupName=Audio Connect
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.20348
OutputDir={#OutputDirPath}
OutputBaseFilename=AudioConnect-Setup-{#AppVersion}-win-x64
SetupIconFile=..\assets\app.ico
UninstallDisplayIcon={app}\AudioConnect.exe
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes
CloseApplicationsFilter=AudioConnect.exe
RestartApplications=no
AppMutex=Local\AudioConnect.Desktop
LicenseFile=..\LICENSE
VersionInfoVersion={#AppVersion}

[Languages]
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Audio Connect"; Filename: "{app}\AudioConnect.exe"
Name: "{autodesktop}\Audio Connect"; Filename: "{app}\AudioConnect.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\AudioConnect.exe"; Description: "{cm:LaunchProgram,Audio Connect}"; Flags: nowait postinstall skipifsilent
