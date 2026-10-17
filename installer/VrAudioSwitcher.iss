#define AppName "VR Audio Switcher"
#define AppVersion GetEnv("APP_VERSION")
#if AppVersion == ""
  #define AppVersion "0.1.0"
#endif

[Setup]
AppId={{6E1B7C52-4E0B-4C58-9B57-0D7F5C3A91AB}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=VirtualyMichael
AppPublisherURL=https://github.com/VirtualyMichael/vr-audio-auto-switcher
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
PrivilegesRequired=lowest
DisableProgramGroupPage=yes
AppMutex=VrAudioSwitcher.SingleInstance
CloseApplications=force
OutputDir=..\dist
OutputBaseFilename=VrAudioSwitcher-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\VrAudioSwitcher.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible

[Tasks]
Name: "autostart"; Description: "Start with Windows (no admin prompt)"; GroupDescription: "Options:"
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Options:"; Flags: unchecked

[Files]
Source: "..\publish\VrAudioSwitcher.exe"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\VrAudioSwitcher.exe"; Parameters: "--show"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\VrAudioSwitcher.exe"; Parameters: "--show"; Tasks: desktopicon

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "VrAudioSwitcher"; ValueData: """{app}\VrAudioSwitcher.exe"""; Tasks: autostart; Flags: uninsdeletevalue

[Run]
Filename: "{app}\VrAudioSwitcher.exe"; Parameters: "--show"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent
