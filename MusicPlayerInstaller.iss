[Setup]
AppName=Music Player
AppVersion=1.0
DefaultDirName={pf}\Music Player
DefaultGroupName=Music Player
OutputBaseFilename=MusicPlayerInstaller
Compression=lzma
SolidCompression=yes
DisableDirPage=no
DisableProgramGroupPage=no
SetupIconFile=app.ico
UninstallDisplayIcon={app}\MusicPlayer.exe

[Files]
; Pull from the publish output folder
Source: "MusicPlayer\bin\Release\app.publish\MusicPlayer.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "MusicPlayer\bin\Release\app.publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "app.ico"; DestDir: "{app}"; Flags: ignoreversion

[Icons]
Name: "{group}\\Music Player"; Filename: "{app}\\MusicPlayer.exe"; IconFilename: "{app}\\app.ico"
Name: "{commondesktop}\\Music Player"; Filename: "{app}\\MusicPlayer.exe"; IconFilename: "{app}\\app.ico"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Run]
Filename: "{app}\\MusicPlayer.exe"; Description: "Launch Music Player"; Flags: nowait postinstall skipifsilent
