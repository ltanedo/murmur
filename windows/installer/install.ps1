# Murmur for Windows: portable install from the latest GitHub release.
#   irm https://github.com/ltanedo/murmur/releases/latest/download/install.ps1 | iex
$ErrorActionPreference = 'Stop'
$repo = 'ltanedo/murmur'
$dest = Join-Path $env:LOCALAPPDATA 'Programs\Murmur'
$zip = Join-Path $env:TEMP 'Murmur-win-x64.zip'
Write-Host "Downloading Murmur…"
Invoke-WebRequest -Uri "https://github.com/$repo/releases/latest/download/Murmur-win-x64.zip" -OutFile $zip
Get-Process Murmur -ErrorAction SilentlyContinue | Stop-Process -Force
New-Item -ItemType Directory -Force -Path $dest | Out-Null
Expand-Archive -Path $zip -DestinationPath $dest -Force
Remove-Item $zip -Force
$startMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Murmur.lnk'
$shortcut = (New-Object -ComObject WScript.Shell).CreateShortcut($startMenu)
$shortcut.TargetPath = Join-Path $dest 'Murmur.exe'
$shortcut.Save()
if (-not (dotnet --list-runtimes 2>$null | Select-String 'Microsoft.WindowsDesktop.App 8\.')) {
  Write-Warning "Murmur needs the .NET 8 Desktop Runtime: https://dotnet.microsoft.com/download/dotnet/8.0"
}
Start-Process (Join-Path $dest 'Murmur.exe')
Write-Host "Installed to $dest. Turn on Settings > Privacy & security > Speech > Online speech recognition, then hold Right Alt to dictate."
