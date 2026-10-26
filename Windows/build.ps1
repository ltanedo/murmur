# Builds Murmur for Windows.
#
#   .\Windows\build.ps1              -> Windows\Murmur\bin\Release\...\Murmur.exe (framework-dependent)
#   .\Windows\build.ps1 -Release     -> dist\windows\Murmur.exe (single file) and dist\Murmur-windows-<version>.zip
#
# Needs the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
[CmdletBinding()]
param([switch]$Release)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$project = Join-Path $root 'Windows\Murmur\Murmur.csproj'

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found. Install the .NET 8 SDK (winget install Microsoft.DotNet.SDK.8) and open a new terminal.'
}

if (-not $Release) {
    dotnet build $project -c Release
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    $exe = Get-ChildItem (Join-Path $root 'Windows\Murmur\bin\Release') -Recurse -Filter Murmur.exe | Select-Object -First 1
    Write-Host "Built $($exe.FullName)"
    exit 0
}

$out = Join-Path $root 'dist\windows'
if (Test-Path $out) { Remove-Item -Recurse -Force $out }
dotnet publish $project -c Release -o $out
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Published $out\Murmur.exe"

[xml]$csproj = Get-Content $project
$version = $csproj.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1
$zip = Join-Path $root "dist\Murmur-windows-$version.zip"
if (Test-Path $zip) { Remove-Item -Force $zip }
Compress-Archive -Path (Join-Path $out '*') -DestinationPath $zip
Write-Host "Packaged $zip"
