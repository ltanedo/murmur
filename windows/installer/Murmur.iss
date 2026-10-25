; Inno Setup script: per-user install, no admin, optional Launch at Login.
; Build: iscc /DVersion=0.2.0 /DPublishDir=..\Murmur.Tray\bin\Release\net8.0-windows10.0.22621.0\win-x64\publish windows\installer\Murmur.iss
#ifndef Version
  #define Version "0.0.0"
#endif
#ifndef PublishDir
  #define PublishDir "..\Murmur.Tray\bin\Release\net8.0-windows10.0.22621.0\win-x64\publish"
#endif

[Setup]
AppId={{B6C2D1E0-3A4F-4C5B-9D8E-7F6A5B4C3D2E}
AppName=Murmur
AppVersion={#Version}
AppPublisher=ltanedo
AppPublisherURL=https://github.com/ltanedo/murmur
DefaultDirName={localappdata}\Programs\Murmur
DefaultGroupName=Murmur
PrivilegesRequired=lowest
OutputBaseFilename=MurmurSetup-{#Version}
OutputDir=..\..\dist
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\Murmur.exe
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
CloseApplications=yes

[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\Murmur"; Filename: "{app}\Murmur.exe"
Name: "{userstartup}\Murmur"; Filename: "{app}\Murmur.exe"; Tasks: startup

[Tasks]
Name: "startup"; Description: "Launch Murmur at login"; GroupDescription: "Startup:"

[Run]
Filename: "{app}\Murmur.exe"; Description: "Start Murmur"; Flags: nowait postinstall skipifsilent

; The .NET 8 Desktop Runtime is required (framework-dependent build). The installer
; checks for it and opens the download page if missing.
[Code]
function IsDotNetDesktopInstalled(): Boolean;
var
  ResultCode: Integer;
begin
  Result := Exec('cmd.exe', '/c dotnet --list-runtimes | findstr /C:"Microsoft.WindowsDesktop.App 8."', '', SW_HIDE, ewWaitUntilTerminated, ResultCode) and (ResultCode = 0);
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsDotNetDesktopInstalled() then
  begin
    if MsgBox('Murmur needs the .NET 8 Desktop Runtime. Open the download page now?', mbConfirmation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet/8.0', '', '', SW_SHOWNORMAL, ewNoWait, ResultCode);
  end;
end;
