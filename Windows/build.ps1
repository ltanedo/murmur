# Builds Murmur for Windows as an MSIX package.
#
#   .\Windows\build.ps1             -> dist\windows\Murmur.msix (signed with a per-machine dev certificate)
#   .\Windows\build.ps1 -Install    -> also installs the Windows App Runtime (experimental) and Murmur for the current user
#   .\Windows\build.ps1 -LayoutOnly -> only publishes the loose package layout (Windows\Murmur\bin\msix\layout)
#
# Needs the .NET 8 SDK: https://dotnet.microsoft.com/download/dotnet/8.0
# makeappx.exe and signtool.exe come from the Microsoft.Windows.SDK.BuildTools NuGet package that the
# project references, so no Visual Studio or Windows SDK install is needed.
[CmdletBinding()]
param([switch]$Install, [switch]$LayoutOnly)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$projectDir = Join-Path $root 'Windows\Murmur'
$project = Join-Path $projectDir 'Murmur.csproj'
$layout = Join-Path $projectDir 'bin\msix\layout'
$dist = Join-Path $root 'dist\windows'
$msix = Join-Path $dist 'Murmur.msix'
$certSubject = 'CN=Murmur Developer'   # must match Identity/@Publisher in Package.appxmanifest

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found. Install the .NET 8 SDK (winget install Microsoft.DotNet.SDK.8) and open a new terminal.'
}

# 1. Publish the app (self-contained .NET, framework-dependent Windows App Runtime) into the package layout.
if (Test-Path $layout) { Remove-Item -Recurse -Force $layout }
dotnet publish $project -c Release -r win-x64 -o $layout
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Copy-Item (Join-Path $projectDir 'Package.appxmanifest') (Join-Path $layout 'AppxManifest.xml')
New-Item -ItemType Directory -Force (Join-Path $layout 'Assets') | Out-Null
Copy-Item (Join-Path $projectDir 'Assets\*.png') (Join-Path $layout 'Assets')
Write-Host "Layout at $layout"
if ($LayoutOnly) { exit 0 }

# 2. Find makeappx/signtool in the NuGet cache.
$nuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$toolsRoot = Get-ChildItem (Join-Path $nuget 'microsoft.windows.sdk.buildtools') -Directory | Sort-Object Name -Descending | Select-Object -First 1
$makeappx = Get-ChildItem $toolsRoot.FullName -Recurse -Filter makeappx.exe | Where-Object FullName -like '*\x64\*' | Select-Object -First 1
$signtool = Get-ChildItem $toolsRoot.FullName -Recurse -Filter signtool.exe | Where-Object FullName -like '*\x64\*' | Select-Object -First 1
if (-not $makeappx -or -not $signtool) { throw 'makeappx.exe / signtool.exe not found; run dotnet restore first.' }

# 3. Pack.
New-Item -ItemType Directory -Force $dist | Out-Null
if (Test-Path $msix) { Remove-Item -Force $msix }
& $makeappx.FullName pack /o /d $layout /p $msix
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# 4. Sign with a self-signed development certificate kept in the current user's store (created on first use).
$cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $certSubject -and $_.NotAfter -gt (Get-Date) } | Select-Object -First 1
if (-not $cert) {
    Write-Host "Creating development signing certificate $certSubject"
    $cert = New-SelfSignedCertificate -Type Custom -Subject $certSubject -KeyUsage DigitalSignature `
        -FriendlyName 'Murmur development signing' -CertStoreLocation Cert:\CurrentUser\My `
        -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}') -NotAfter (Get-Date).AddYears(3)
}
& $signtool.FullName sign /fd SHA256 /sha1 $cert.Thumbprint $msix
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
Write-Host "Built $msix"

if (-not $Install) {
    Write-Host 'To install for the current user, run .\Windows\build.ps1 -Install'
    exit 0
}

# 5. Install the experimental Windows App Runtime framework the manifest depends on, then Murmur.
[xml]$manifest = Get-Content (Join-Path $projectDir 'Package.appxmanifest')
$dep = $manifest.Package.Dependencies.PackageDependency
$installed = Get-AppxPackage -Name $dep.Name | Where-Object { [version]$_.Version -ge [version]$dep.MinVersion }
if (-not $installed) {
    $runtime = Get-ChildItem (Join-Path $nuget 'microsoft.windowsappsdk.runtime') -Directory | Sort-Object Name -Descending | Select-Object -First 1
    $fw = Get-ChildItem (Join-Path $runtime.FullName 'tools\MSIX\win10-x64') -Filter "$($dep.Name).msix" | Select-Object -First 1
    if (-not $fw) { throw "Windows App Runtime package $($dep.Name) not found in the NuGet cache." }
    Write-Host "Installing $($fw.Name)"
    Add-AppxPackage -Path $fw.FullName
}

$existing = Get-AppxPackage -Name 'Murmur.Dictation'
if ($existing) { Remove-AppxPackage -Package $existing.PackageFullName }

$devMode = (Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue).AllowDevelopmentWithoutDevLicense -eq 1
if ($devMode) {
    # Developer Mode lets Windows register the loose, unsigned layout directly (the same thing Visual Studio's F5 does).
    Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml')
    Write-Host 'Registered Murmur from the layout folder (Developer Mode). Start it from the Start menu.'
    exit 0
}

# Without Developer Mode the signed package is installed, which needs the dev certificate in the
# current user's Trusted Root store. Windows shows a one-time confirmation dialog for that.
if (-not (Get-ChildItem Cert:\CurrentUser\Root | Where-Object Thumbprint -eq $cert.Thumbprint)) {
    Write-Host 'Adding the development certificate to Trusted Root Certification Authorities (current user); confirm the Windows prompt.'
    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store('Root', 'CurrentUser')
    $store.Open('ReadWrite'); $store.Add($cert); $store.Close()
}
Add-AppxPackage -Path $msix
Write-Host 'Installed Murmur. Start it from the Start menu.'
